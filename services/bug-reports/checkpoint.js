import {validCheckpoint} from "./validation.js";

// Both intake and download use the same bounded decoder. No saved content is executed.
export async function decodeCheckpoint(checkpoint) {
  if (!validCheckpoint(checkpoint) || !checkpoint.available) throw new Error("checkpoint_unavailable");
  const raw=atob(checkpoint.data);
  if(raw.length>110000)throw new Error("invalid_checkpoint");
  const bytes=Uint8Array.from(raw,c=>c.charCodeAt(0));
  const reader=new Blob([bytes]).stream().pipeThrough(new DecompressionStream("gzip")).getReader();
  const chunks=[];let length=0;
  try {
    while(true){const {done,value}=await reader.read();if(done)break;length+=value.length;
      if(length>2000000){await reader.cancel();throw new Error("invalid_checkpoint")};chunks.push(value)}
  }finally{reader.releaseLock()}
  if(length!==checkpoint.plain_bytes)throw new Error("invalid_checkpoint");
  const plain=new Uint8Array(length);let offset=0;for(const chunk of chunks){plain.set(chunk,offset);offset+=chunk.length}
  const hash=Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256",plain)),b=>b.toString(16).padStart(2,"0")).join("");
  if(hash!==checkpoint.sha256)throw new Error("invalid_checkpoint");
  const data=JSON.parse(new TextDecoder("utf-8",{fatal:true}).decode(plain));
  if(!data||typeof data!=="object"||!Number.isInteger(data.schema_version)||!Array.isArray(data.players)
    ||data.players.length!==checkpoint.player_count||!Array.isArray(data.acts)||!data.rng)throw new Error("invalid_checkpoint");
  if(data.players.some((p,i)=>p.net_id!==i+1))throw new Error("invalid_checkpoint_identity");
  return plain;
}
