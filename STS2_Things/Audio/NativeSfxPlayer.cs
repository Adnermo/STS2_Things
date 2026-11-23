using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Saves;
using FileAccess = Godot.FileAccess;

namespace STS2_Things.Audio;

public static class NativeSfxPlayer
{
    private const int OneShotPoolCapacity = 12;

    // 仅缓存短音效（禁止常驻 res://music/... 长曲目），读取前验证 IsInstanceValid 以避免与 PreloadManager 卸载冲突
    private static readonly Dictionary<string, AudioStream> _streamCache = new(StringComparer.Ordinal);
    private static readonly object _streamCacheLock = new();
    private static readonly List<AudioStreamPlayer> _pooledPlayers = new(OneShotPoolCapacity);
    private static readonly HashSet<AudioStreamPlayer> _busyPooledPlayers = new();
    private static readonly List<AudioStreamPlayer> _overflowPlayers = new();
    private static readonly object _activePlayersLock = new();
    // 缓存目录到候选文件列表，避免每次播放随机变体都进行IO遍历
    private static readonly Dictionary<string, string[]> _variantCache = new(StringComparer.Ordinal);
    private static readonly object _variantCacheLock = new();
    // 独立于游戏玩法 RNG 的本地音频变体 RNG。
    private static readonly RandomNumberGenerator _variantRng = new();
    private static readonly object _variantRngLock = new();
    private static AudioStreamPlayer? _musicPlayer;
    private static Tween? _musicFadeTween;
    private static string? _musicPath;
    private static float _musicAuthoredVolumeDb = -6f;
    private static int _musicGeneration;
    private static bool _musicStartPending;
    private static AudioStreamPlayer? _seqLooper;
    private static string[]? _seqPaths;
    private static int _seqIndex;
    private static float _seqVolumeDb;

    static NativeSfxPlayer()
    {
        _variantRng.Randomize();
    }

    public static bool HasActiveMusic =>
        _musicStartPending || (_musicPlayer != null && GodotObject.IsInstanceValid(_musicPlayer));

    public static string? ActiveMusicPath => HasActiveMusic ? _musicPath : null;

    public static bool IsMusicFadingOut =>
        _musicFadeTween != null && GodotObject.IsInstanceValid(_musicFadeTween) && _musicFadeTween.IsRunning();

    /// <summary>
    /// Rolls a presentation-only probability using the audio RNG. This stream is
    /// deliberately isolated from combat RNG so SFX variation cannot affect saves
    /// or multiplayer simulation state.
    /// </summary>
    public static bool RollChance(float chance)
    {
        if (!float.IsFinite(chance) || chance <= 0f) return false;
        if (chance >= 1f) return true;
        lock (_variantRngLock)
        {
            return _variantRng.Randf() < chance;
        }
    }

    private static bool CanPlayAudio(bool allowCombatEnding = false)
    {
        return !NonInteractiveMode.IsActive
               && (allowCombatEnding || !CombatManager.Instance.IsEnding);
    }

    private static bool IsMusicPath(string resPath) =>
        resPath.StartsWith("res://music/", StringComparison.OrdinalIgnoreCase);

    public static void Preload(string resPath)
    {
        LoadStream(resPath, cacheStream: !IsMusicPath(resPath));
    }

    public static void Play(
        string resPath,
        string bus = "Master",
        float volumeDb = 6f,
        bool allowCombatEnding = false)
    {
        if (!CanPlayAudio(allowCombatEnding)) return;
        if (!HasAudioExtension(resPath))
        {
            var resolved = ResolveRandomVariant(resPath);
            if (resolved != null)
                PlayExact(resolved, bus, volumeDb, allowCombatEnding);
            else
                Log.Warn($"[NativeSfxPlayer] No variant found for: {resPath}");
            return;
        }

        PlayExact(resPath, bus, volumeDb, allowCombatEnding);
    }

    internal static AudioStream? LoadLoopingMusicStream(string resPath)
    {
        var stream = LoadStream(resPath, cacheStream: false);
        return EnsureLoopingMusicStream(stream);
    }

    internal static AudioStream? EnsureLoopingMusicStream(AudioStream? stream)
    {
        if (stream == null || !GodotObject.IsInstanceValid(stream))
            return null;

        if (stream is AudioStreamWav wav)
        {
            // 已烘焙 Forward 循环点的 .sample 直接复用，避免整首 48kHz WAV 内存翻倍复制。
            if (wav.LoopMode == AudioStreamWav.LoopModeEnum.Forward && wav.LoopEnd > wav.LoopBegin)
                return wav;

            var wavCopy = (AudioStreamWav)wav.Duplicate();
            if (wavCopy.LoopEnd <= wavCopy.LoopBegin)
            {
                int totalSamples = (int)Math.Round(wavCopy.GetLength() * Math.Max(1, wavCopy.MixRate));
                if (totalSamples > wavCopy.LoopBegin)
                    wavCopy.LoopEnd = totalSamples;
            }
            if (wavCopy.LoopEnd > wavCopy.LoopBegin)
                wavCopy.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            return wavCopy;
        }

        return stream;
    }

    internal static void RefreshMusicVolume()
    {
        if (_musicPath != null && _musicPlayer != null && GodotObject.IsInstanceValid(_musicPlayer) && _musicPlayer.Playing)
        {
            _musicPlayer.VolumeDb = ResolveMusicVolumeDb(_musicAuthoredVolumeDb);
        }
    }

    public static void PlayMusic(string resPath, string bus = "Master", float volumeDb = -6f)
    {
        try
        {
            if (!CanPlayAudio()) return;
            var stream = LoadLoopingMusicStream(resPath);
            if (stream == null)
            {
                Log.Warn($"[NativeSfxPlayer] PlayMusic: stream is null for {resPath}");
                return;
            }

            int gen = Interlocked.Increment(ref _musicGeneration);
            _musicPath = resPath;
            _musicAuthoredVolumeDb = volumeDb;
            _musicStartPending = true;

            Callable.From(() =>
            {
                try
                {
                    if (gen != Volatile.Read(ref _musicGeneration) ||
                        _musicPath == null ||
                        !CanPlayAudio() ||
                        !string.Equals(_musicPath, resPath, StringComparison.Ordinal))
                    {
                        if (gen == Volatile.Read(ref _musicGeneration))
                        {
                            _musicStartPending = false;
                            _musicPath = null;
                        }
                        return;
                    }

                    AudioStream? liveStream = GodotObject.IsInstanceValid(stream)
                        ? stream
                        : LoadLoopingMusicStream(resPath);
                    if (liveStream == null || !GodotObject.IsInstanceValid(liveStream))
                    {
                        if (gen == Volatile.Read(ref _musicGeneration))
                        {
                            _musicStartPending = false;
                            _musicPath = null;
                        }
                        return;
                    }

                    var tree = Engine.GetMainLoop() as SceneTree;
                    if (tree?.Root == null || !GodotObject.IsInstanceValid(tree.Root))
                    {
                        if (gen == Volatile.Read(ref _musicGeneration))
                        {
                            _musicStartPending = false;
                            _musicPath = null;
                        }
                        Log.Warn("[NativeSfxPlayer] PlayMusic: SceneTree Root is null or invalid!");
                        return;
                    }

                    ReleaseMusicPlayer();

                    var player = new AudioStreamPlayer
                    {
                        Stream = liveStream,
                        Bus = bus,
                        VolumeDb = ResolveMusicVolumeDb(_musicAuthoredVolumeDb)
                    };
                    _musicPlayer = player;
                    _musicStartPending = false;

                    tree.Root.AddChild(player);
                    player.Finished += () =>
                    {
                        if (_musicPlayer == player &&
                            gen == Volatile.Read(ref _musicGeneration) &&
                            _musicPath != null &&
                            GodotObject.IsInstanceValid(player) &&
                            player.IsInsideTree())
                        {
                            player.VolumeDb = ResolveMusicVolumeDb(_musicAuthoredVolumeDb);
                            player.Play();
                        }
                    };
                    player.Play();
                }
                catch (Exception ex)
                {
                    if (gen == Volatile.Read(ref _musicGeneration))
                    {
                        _musicStartPending = false;
                        _musicPath = null;
                    }
                    Log.Warn($"[NativeSfxPlayer] Main-thread PlayMusic failed: {ex.Message}");
                }
            }).CallDeferred();
        }
        catch (Exception e)
        {
            Log.Warn($"[NativeSfxPlayer] PlayMusic exception: {e.Message}");
        }
    }

    public static void FadeOutAndStopMusic(float durationSeconds = 1.5f)
    {
        int gen = Interlocked.Increment(ref _musicGeneration);
        _musicPath = null;
        _musicStartPending = false;

        void BeginFade()
        {
            try
            {
                if (gen != Volatile.Read(ref _musicGeneration))
                    return;

                AudioStreamPlayer? player = _musicPlayer;
                if (player == null || !GodotObject.IsInstanceValid(player))
                {
                    ReleaseMusicPlayer();
                    return;
                }

                if (durationSeconds <= 0f || !player.Playing || !player.IsInsideTree())
                {
                    ReleaseMusicPlayer();
                    return;
                }

                KillMusicFadeTween();
                Tween tween = player.CreateTween();
                _musicFadeTween = tween;
                tween.SetPauseMode(Tween.TweenPauseMode.Process);
                tween.TweenProperty(
                        player,
                        AudioStreamPlayer.PropertyName.VolumeLinear.ToString(),
                        0f,
                        durationSeconds)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.In);
                tween.Finished += () =>
                {
                    if (ReferenceEquals(_musicPlayer, player))
                    {
                        ReleaseMusicPlayer();
                    }
                    else
                    {
                        DisposeDetachedMusicPlayer(player);
                    }
                };
            }
            catch (Exception ex)
            {
                Log.Warn($"[NativeSfxPlayer] FadeOutAndStopMusic failed: {ex.Message}");
                ReleaseMusicPlayer();
            }
        }

        try
        {
            if (NGame.IsMainThread())
            {
                BeginFade();
            }
            else
            {
                Callable.From(BeginFade).CallDeferred();
            }
        }
        catch (Exception e)
        {
            Log.Warn($"[NativeSfxPlayer] FadeOutAndStopMusic dispatch error: {e.Message}");
        }
    }

    public static void StopMusic()
    {
        int gen = Interlocked.Increment(ref _musicGeneration);
        _musicPath = null;
        _musicStartPending = false;
        try
        {
            if (NGame.IsMainThread())
            {
                ReleaseMusicPlayer();
                return;
            }

            Callable.From(() =>
            {
                try
                {
                    if (gen == Volatile.Read(ref _musicGeneration))
                        ReleaseMusicPlayer();
                }
                catch (Exception ex)
                {
                    Log.Warn($"[NativeSfxPlayer] StopMusic failed: {ex.Message}");
                }
            }).CallDeferred();
        }
        catch (Exception e)
        {
            Log.Warn($"[NativeSfxPlayer] StopMusic dispatch error: {e.Message}");
        }
    }

    private static void KillMusicFadeTween()
    {
        if (_musicFadeTween != null)
        {
            if (GodotObject.IsInstanceValid(_musicFadeTween))
                _musicFadeTween.Kill();
            _musicFadeTween = null;
        }
    }

    private static void DisposeDetachedMusicPlayer(AudioStreamPlayer? player)
    {
        if (player == null || !GodotObject.IsInstanceValid(player))
            return;

        if (player.Playing) player.Stop();
        player.Stream = null;
        if (player.IsInsideTree()) player.QueueFree();
    }

    private static void ReleaseMusicPlayer()
    {
        KillMusicFadeTween();
        if (_musicPlayer == null)
            return;

        DisposeDetachedMusicPlayer(_musicPlayer);
        _musicPlayer = null;
    }

    /// <summary>
    ///     依次循环播放多个音频文件
    /// </summary>
    public static void PlaySequentialLoop(string[]? paths, float volumeDb = -10f)
    {
        StopSequentialLoop();
        if (!CanPlayAudio() || paths == null || paths.Length == 0) return;
        _seqPaths = paths;
        _seqIndex = 0;
        _seqVolumeDb = volumeDb;
        PlayNextInSequence();
    }

    public static void StopSequentialLoop()
    {
        try
        {
            Callable.From(() =>
            {
                try
                {
                    if (_seqLooper != null)
                    {
                        if (GodotObject.IsInstanceValid(_seqLooper))
                        {
                            if (_seqLooper.Playing) _seqLooper.Stop();
                            _seqLooper.Stream = null;
                            if (_seqLooper.IsInsideTree()) _seqLooper.QueueFree();
                        }
                        _seqLooper = null;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn($"[NativeSfxPlayer] StopSequentialLoop failed: {ex.Message}");
                }
            }).CallDeferred();
        }
        catch (Exception e)
        {
            Log.Warn($"[NativeSfxPlayer] StopSequentialLoop dispatch error: {e.Message}");
        }

        _seqPaths = null;
        _seqIndex = 0;
    }

    private static void PlayNextInSequence()
    {
        try
        {
            if (!CanPlayAudio())
            {
                StopSequentialLoop();
                return;
            }
            var paths = _seqPaths;
            if (paths == null || paths.Length == 0)
            {
                StopSequentialLoop();
                return;
            }

            if (_seqIndex >= paths.Length) _seqIndex = 0;
            var path = paths[_seqIndex];
            _seqIndex++;
            if (_seqIndex >= paths.Length) _seqIndex = 0;

            var stream = LoadStream(path, cacheStream: !IsMusicPath(path));
            if (stream == null)
            {
                Log.Warn($"[NativeSfxPlayer] SeqLoop stream is null for {path}");
                return;
            }

            Callable.From(() =>
            {
                try
                {
                    if (_seqPaths == null || !CanPlayAudio())
                        return;

                    AudioStream? liveStream = GodotObject.IsInstanceValid(stream)
                        ? stream
                        : LoadStream(path, cacheStream: !IsMusicPath(path));
                    if (liveStream == null || !GodotObject.IsInstanceValid(liveStream))
                        return;

                    var tree = Engine.GetMainLoop() as SceneTree;
                    if (tree?.Root == null || !GodotObject.IsInstanceValid(tree.Root)) return;

                    if (_seqLooper != null && GodotObject.IsInstanceValid(_seqLooper))
                    {
                        if (_seqLooper.Playing) _seqLooper.Stop();
                        _seqLooper.Stream = null;
                        if (_seqLooper.IsInsideTree()) _seqLooper.QueueFree();
                    }

                    float effectiveLoopDb = ResolveSfxVolumeDb(_seqVolumeDb) ?? -80f;
                    var looper = new AudioStreamPlayer { Stream = liveStream, Bus = "Master", VolumeDb = effectiveLoopDb };
                    _seqLooper = looper;
                    tree.Root.AddChild(looper);
                    looper.Finished += () =>
                    {
                        if (_seqLooper == looper && _seqPaths != null &&
                            GodotObject.IsInstanceValid(_seqLooper) && _seqLooper.IsInsideTree())
                        {
                            PlayNextInSequence();
                        }
                    };
                    looper.Play();
                }
                catch (Exception ex)
                {
                    Log.Warn($"[NativeSfxPlayer] PlayNextInSequence main-thread failed: {ex.Message}");
                }
            }).CallDeferred();
        }
        catch (Exception e)
        {
            Log.Warn($"[NativeSfxPlayer] PlayNextInSequence dispatch error: {e.Message}");
        }
    }

    private static AudioStream? TryGetPreloadedStream(string resPath)
    {
        try
        {
            if (PreloadManager.Cache.ContainsKey(resPath))
            {
                var resource = PreloadManager.Cache.GetAsset<Resource>(resPath);
                if (resource is AudioStream stream && GodotObject.IsInstanceValid(stream))
                    return stream;
            }
        }
        catch (Exception)
        {
            // Fall through to standard loading if PreloadManager is not active or type differs.
        }

        return null;
    }

    private static AudioStream? LoadStream(string resPath, bool cacheStream = true)
    {
        bool allowCache = cacheStream && !IsMusicPath(resPath);

        // 优先复用 PreloadManager 当前房间已预加载的有效资源，避免跨房间强引用或使用已卸载实例。
        var preloaded = TryGetPreloadedStream(resPath);
        if (preloaded != null)
            return preloaded;

        if (allowCache)
        {
            lock (_streamCacheLock)
            {
                if (_streamCache.TryGetValue(resPath, out var cached))
                {
                    if (GodotObject.IsInstanceValid(cached))
                        return cached;
                    _streamCache.Remove(resPath);
                }
            }
        }

        try
        {
            if (ResourceLoader.Exists(resPath))
            {
                var stream = ResourceLoader.Load<AudioStream>(resPath);
                if (stream != null && GodotObject.IsInstanceValid(stream))
                {
                    if (allowCache)
                    {
                        lock (_streamCacheLock)
                        {
                            _streamCache[resPath] = stream;
                        }
                    }
                    return stream;
                }
            }

            var bytes = ReadBytesFromDisk(resPath);
            if (bytes != null && bytes.Length > 0)
            {
                var ext = Path.GetExtension(resPath).ToLowerInvariant();
                AudioStream? stream = ext switch
                {
                    ".ogg" => AudioStreamOggVorbis.LoadFromBuffer(bytes),
                    ".mp3" => AudioStreamMP3.LoadFromBuffer(bytes),
                    ".wav" => LoadWavFromBuffer(bytes),
                    _ => null
                };
                if (stream != null && GodotObject.IsInstanceValid(stream))
                {
                    if (allowCache)
                    {
                        lock (_streamCacheLock)
                        {
                            _streamCache[resPath] = stream;
                        }
                    }
                    return stream;
                }
            }

            Log.Warn($"[NativeSfxPlayer] Failed to load: {resPath}");
            return null;
        }
        catch (Exception e)
        {
            Log.Warn($"[NativeSfxPlayer] Load error {resPath}: {e.Message}");
            return null;
        }
    }

    private static byte[]? ReadBytesFromDisk(string resPath)
    {
        var absPath = ToAbsolutePath(resPath);
        if (File.Exists(absPath)) return File.ReadAllBytes(absPath);
        var fa = FileAccess.Open(resPath, FileAccess.ModeFlags.Read);
        if (fa != null)
        {
            var len = fa.GetLength();
            if (len > 0)
            {
                var buf = fa.GetBuffer((long)len);
                fa.Dispose();
                if (buf != null && buf.Length > 0) return buf;
            }

            fa.Dispose();
        }

        return null;
    }

    private static AudioStreamWav? LoadWavFromBuffer(byte[] bytes)
    {
        if (bytes.Length < 44) return null;
        var riff = Encoding.ASCII.GetString(bytes, 0, 4);
        var wave = Encoding.ASCII.GetString(bytes, 8, 4);
        if (riff != "RIFF" || wave != "WAVE") return null;
        var stream = new AudioStreamWav();
        var offset = 12;
        byte[]? dataBytes = null;
        while (offset < bytes.Length - 8)
        {
            var chunkId = Encoding.ASCII.GetString(bytes, offset, 4);
            var chunkSize = BitConverter.ToInt32(bytes, offset + 4);
            if (chunkId == "fmt ")
            {
                int channels = BitConverter.ToInt16(bytes, offset + 10);
                var sampleRate = BitConverter.ToInt32(bytes, offset + 12);
                int bitsPerSample = BitConverter.ToInt16(bytes, offset + 22);
                if (bitsPerSample == 32) return null;
                stream.Format = bitsPerSample switch
                {
                    8 => AudioStreamWav.FormatEnum.Format8Bits,
                    _ => AudioStreamWav.FormatEnum.Format16Bits
                };
                stream.MixRate = sampleRate;
                stream.Stereo = channels == 2;
            }
            else if (chunkId == "data")
            {
                var dataSize = chunkSize;
                dataBytes = new byte[dataSize];
                Array.Copy(bytes, offset + 8, dataBytes, 0, dataSize);
                break;
            }

            offset += 8 + chunkSize;
            if (chunkSize % 2 == 1) offset++;
        }

        if (dataBytes == null) return null;
        stream.Data = dataBytes;
        int bytesPerSampleFrame = Math.Max(1, (stream.Stereo ? 2 : 1) * (stream.Format == AudioStreamWav.FormatEnum.Format8Bits ? 1 : 2));
        stream.LoopBegin = 0;
        stream.LoopEnd = dataBytes.Length / bytesPerSampleFrame;
        return stream;
    }

    private static float ResolveMusicVolumeDb(float authoredVolumeDb)
    {
        if (SaveManager.Instance?.SettingsSave is not { } settings)
            return authoredVolumeDb;
        float gain = Math.Clamp(settings.VolumeMaster, 0f, 1f) * Math.Clamp(settings.VolumeBgm, 0f, 1f);
        if (gain <= 0.0001f)
            return -80f;
        // SettingsSave defaults to 0.5 Master * 0.5 BGM = 0.25. Cap upward gain at 0 dBFS.
        return Math.Min(0f, authoredVolumeDb + Mathf.LinearToDb(gain / 0.25f));
    }

    private static float? ResolveSfxVolumeDb(float baseVolumeDb)
    {
        if (SaveManager.Instance?.SettingsSave is not { } settings)
            return baseVolumeDb;
        float gain = Math.Clamp(settings.VolumeMaster, 0f, 1f) * Math.Clamp(settings.VolumeSfx, 0f, 1f);
        if (gain <= 0.0001f)
            return null;
        // SettingsSave defaults to 0.5 Master * 0.5 SFX = 0.25 -> 0 dB offset; cap upward gain to prevent clipping.
        return Math.Min(Math.Max(0f, baseVolumeDb), baseVolumeDb + Mathf.LinearToDb(gain / 0.25f));
    }

    private static void PlayExact(string resPath, string bus, float volumeDb, bool allowCombatEnding = false)
    {
        try
        {
            if (ResolveSfxVolumeDb(volumeDb) == null)
                return;

            var stream = LoadStream(resPath, cacheStream: true);
            if (stream == null) return;

            if (NGame.IsMainThread() &&
                Engine.GetMainLoop() is SceneTree { Root: { } readyRoot } &&
                GodotObject.IsInstanceValid(readyRoot) &&
                readyRoot.IsNodeReady())
            {
                PlayOneShotOnMainThread(resPath, stream, bus, volumeDb, allowCombatEnding);
                return;
            }

            Callable.From(() =>
            {
                PlayOneShotOnMainThread(resPath, stream, bus, volumeDb, allowCombatEnding);
            }).CallDeferred();
        }
        catch (Exception e)
        {
            Log.Warn($"[NativeSfxPlayer] PlayExact dispatch failed: {e.Message}");
        }
    }

    private static void PlayOneShotOnMainThread(
        string resPath,
        AudioStream stream,
        string bus,
        float volumeDb,
        bool allowCombatEnding)
    {
        AudioStreamPlayer? acquiredPlayer = null;
        bool isPooled = false;
        try
        {
            if (!CanPlayAudio(allowCombatEnding))
                return;
            if (ResolveSfxVolumeDb(volumeDb) is not { } mainThreadVolumeDb)
                return;

            AudioStream? liveStream = GodotObject.IsInstanceValid(stream)
                ? stream
                : LoadStream(resPath, cacheStream: true);
            if (liveStream == null || !GodotObject.IsInstanceValid(liveStream))
                return;

            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree?.Root == null || !GodotObject.IsInstanceValid(tree.Root))
                return;

            acquiredPlayer = AcquireOneShotPlayer(tree.Root, out isPooled);
            acquiredPlayer.Stream = liveStream;
            acquiredPlayer.Bus = bus;
            acquiredPlayer.VolumeDb = mainThreadVolumeDb;
            acquiredPlayer.Play();
        }
        catch (Exception ex)
        {
            if (acquiredPlayer != null)
            {
                if (GodotObject.IsInstanceValid(acquiredPlayer))
                {
                    acquiredPlayer.Stream = null;
                    if (!isPooled && acquiredPlayer.IsInsideTree())
                        acquiredPlayer.QueueFree();
                }

                lock (_activePlayersLock)
                {
                    if (isPooled)
                        _busyPooledPlayers.Remove(acquiredPlayer);
                    else
                        _overflowPlayers.Remove(acquiredPlayer);
                }
            }
            Log.Warn($"[NativeSfxPlayer] PlayExact main-thread failed: {ex.Message}");
        }
    }

    private static AudioStreamPlayer AcquireOneShotPlayer(Node root, out bool isPooled)
    {
        int createPoolIndex = -1;
        lock (_activePlayersLock)
        {
            for (int i = _pooledPlayers.Count - 1; i >= 0; i--)
            {
                var candidate = _pooledPlayers[i];
                if (!GodotObject.IsInstanceValid(candidate) || !candidate.IsInsideTree())
                {
                    _busyPooledPlayers.Remove(candidate);
                    _pooledPlayers.RemoveAt(i);
                    continue;
                }

                if (!_busyPooledPlayers.Contains(candidate) && !candidate.Playing)
                {
                    _busyPooledPlayers.Add(candidate);
                    isPooled = true;
                    return candidate;
                }
            }

            if (_pooledPlayers.Count < OneShotPoolCapacity)
                createPoolIndex = _pooledPlayers.Count;
        }

        if (createPoolIndex >= 0)
        {
            var pooled = new AudioStreamPlayer { Name = $"ThingsSfxPool_{createPoolIndex}" };
            pooled.Finished += () => OnPooledPlayerFinished(pooled);
            pooled.TreeExiting += () => OnPooledPlayerTreeExiting(pooled);
            root.AddChild(pooled);
            lock (_activePlayersLock)
            {
                _pooledPlayers.Add(pooled);
                _busyPooledPlayers.Add(pooled);
            }
            isPooled = true;
            return pooled;
        }

        var overflow = new AudioStreamPlayer();
        root.AddChild(overflow);
        lock (_activePlayersLock)
        {
            _overflowPlayers.Add(overflow);
        }
        overflow.Finished += () =>
        {
            if (GodotObject.IsInstanceValid(overflow))
            {
                overflow.Stream = null;
                if (overflow.IsInsideTree())
                    overflow.QueueFree();
            }
            lock (_activePlayersLock)
            {
                _overflowPlayers.Remove(overflow);
            }
        };
        isPooled = false;
        return overflow;
    }

    private static void OnPooledPlayerFinished(AudioStreamPlayer player)
    {
        if (GodotObject.IsInstanceValid(player))
            player.Stream = null;
        lock (_activePlayersLock)
        {
            _busyPooledPlayers.Remove(player);
        }
    }

    private static void OnPooledPlayerTreeExiting(AudioStreamPlayer player)
    {
        if (GodotObject.IsInstanceValid(player))
        {
            if (player.Playing) player.Stop();
            player.Stream = null;
        }
        lock (_activePlayersLock)
        {
            _busyPooledPlayers.Remove(player);
            _pooledPlayers.Remove(player);
        }
    }

    /// <summary>
    /// 清理所有活跃的音效播放器，防止资源泄漏。
    /// 由独立的 CombatRoomAudioCleanupPatch 在房间退出时调用。
    /// </summary>
    public static void CleanupActivePlayers()
    {
        try
        {
            Callable.From(() =>
            {
                try
                {
                    List<AudioStreamPlayer> pooledSnapshot;
                    List<AudioStreamPlayer> overflowSnapshot;
                    lock (_activePlayersLock)
                    {
                        pooledSnapshot = new List<AudioStreamPlayer>(_pooledPlayers);
                        overflowSnapshot = new List<AudioStreamPlayer>(_overflowPlayers);
                        _overflowPlayers.Clear();
                        _busyPooledPlayers.Clear();
                    }

                    var deadPooled = new List<AudioStreamPlayer>();
                    foreach (var player in pooledSnapshot)
                    {
                        if (GodotObject.IsInstanceValid(player))
                        {
                            if (player.Playing) player.Stop();
                            player.Stream = null;
                            if (!player.IsInsideTree())
                                deadPooled.Add(player);
                        }
                        else
                        {
                            deadPooled.Add(player);
                        }
                    }

                    if (deadPooled.Count > 0)
                    {
                        lock (_activePlayersLock)
                        {
                            foreach (var dead in deadPooled)
                                _pooledPlayers.Remove(dead);
                        }
                    }

                    foreach (var player in overflowSnapshot)
                    {
                        if (GodotObject.IsInstanceValid(player))
                        {
                            if (player.Playing) player.Stop();
                            player.Stream = null;
                            if (player.IsInsideTree()) player.QueueFree();
                        }
                    }

                    PruneDisposedStreamCacheEntries();
                }
                catch (Exception ex)
                {
                    Log.Warn($"[NativeSfxPlayer] CleanupActivePlayers main-thread failed: {ex.Message}");
                }
            }).CallDeferred();
        }
        catch (Exception e)
        {
            Log.Warn($"[NativeSfxPlayer] CleanupActivePlayers dispatch error: {e.Message}");
        }
    }

    private static void PruneDisposedStreamCacheEntries()
    {
        lock (_streamCacheLock)
        {
            if (_streamCache.Count == 0)
                return;

            List<string>? staleKeys = null;
            foreach (var kv in _streamCache)
            {
                if (!GodotObject.IsInstanceValid(kv.Value))
                {
                    staleKeys ??= new List<string>();
                    staleKeys.Add(kv.Key);
                }
            }

            if (staleKeys != null)
            {
                foreach (var key in staleKeys)
                    _streamCache.Remove(key);
            }
        }
    }

    private static string ToAbsolutePath(string path)
    {
        if (path.StartsWith("res://"))
        {
            var modRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                ?? AppContext.BaseDirectory;
            return Path.GetFullPath(Path.Combine(modRoot, path["res://".Length..]));
        }

        return Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
    }

    private static string? ResolveRandomVariant(string prefixPath)
    {
        var lastSlash = prefixPath.LastIndexOf('/');
        if (lastSlash < 0) return null;
        var dir = prefixPath[..lastSlash];
        var filePrefix = prefixPath[(lastSlash + 1)..];
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(filePrefix)) return null;

        // 缓存 key: "dir|prefix"
        var cacheKey = $"{dir}|{filePrefix}";
        lock (_variantCacheLock)
        {
            if (_variantCache.TryGetValue(cacheKey, out var cachedCandidates))
                return PickVariant(cachedCandidates, filePrefix);
        }

        var list = new List<string>();
        DirAccess? da = DirAccess.Open(dir);
        if (da != null)
        {
            da.ListDirBegin();
            var fileName = da.GetNext();
            while (fileName != string.Empty)
            {
                if (!da.CurrentIsDir())
                {
                    var actualName = fileName.EndsWith(".import") ? fileName[..^".import".Length] : fileName;
                    if (IsAudioFile(actualName) &&
                        actualName.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var nameNoExt = actualName[..actualName.LastIndexOf('.')];
                        if (nameNoExt.Equals(filePrefix, StringComparison.OrdinalIgnoreCase)
                            || (nameNoExt.StartsWith(filePrefix + "-") && nameNoExt.Length > filePrefix.Length + 1 &&
                                char.IsDigit(nameNoExt[filePrefix.Length + 1])))
                            list.Add(dir + "/" + actualName);
                    }
                }

                fileName = da.GetNext();
            }

            da.ListDirEnd();
        }

        if (list.Count == 0)
        {
            var absDir = ToAbsolutePath(dir);
            if (Directory.Exists(absDir))
                foreach (var filePath in Directory.GetFiles(absDir))
                {
                    var nameNoExt = Path.GetFileNameWithoutExtension(filePath);
                    if ((nameNoExt.Equals(filePrefix, StringComparison.OrdinalIgnoreCase)
                         || (nameNoExt.StartsWith(filePrefix + "-") && nameNoExt.Length > filePrefix.Length + 1 &&
                             char.IsDigit(nameNoExt[filePrefix.Length + 1])))
                        && IsAudioFile(Path.GetExtension(filePath)))
                        list.Add(dir + "/" + Path.GetFileName(filePath));
                }
        }

        var candidates = list.ToArray();
        lock (_variantCacheLock)
        {
            _variantCache[cacheKey] = candidates;
        }

        return PickVariant(candidates, filePrefix);
    }

    private static string? PickVariant(string[] candidates, string filePrefix)
    {
        if (candidates == null || candidates.Length == 0) return null;
        var exact = Array.Find(candidates, c =>
        {
            var name = c[(c.LastIndexOf('/') + 1)..];
            var noExt = name[..name.LastIndexOf('.')];
            return noExt.Equals(filePrefix, StringComparison.OrdinalIgnoreCase);
        });
        if (exact != null) return exact;
        lock (_variantRngLock)
        {
            return candidates[_variantRng.RandiRange(0, candidates.Length - 1)];
        }
    }

    private static bool HasAudioExtension(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".ogg" || ext == ".wav" || ext == ".mp3";
    }

    private static bool IsAudioFile(string fileName)
    {
        if (!fileName.Contains('.')) return false;
        var ext = fileName[fileName.LastIndexOf('.')..].ToLowerInvariant();
        return ext == ".ogg" || ext == ".wav" || ext == ".mp3";
    }
}
