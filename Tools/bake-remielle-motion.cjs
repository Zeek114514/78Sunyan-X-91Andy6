// Bake the pinned CC0 glTF source into compact engine-independent world-space tracks.
const fs = require('fs');
const dir = 'Artifacts/Remielle/';
const data = JSON.parse(fs.readFileSync(dir + 'AnimationLibrary.gltf'));
const binary = fs.readFileSync(dir + 'AnimationLibrary.bin');
const mul = (a,b) => [a[3]*b[0]+a[0]*b[3]+a[1]*b[2]-a[2]*b[1],a[3]*b[1]-a[0]*b[2]+a[1]*b[3]+a[2]*b[0],a[3]*b[2]+a[0]*b[1]-a[1]*b[0]+a[2]*b[3],a[3]*b[3]-a[0]*b[0]-a[1]*b[1]-a[2]*b[2]];
const rotate = (q,v) => mul(mul(q,[...v,0]),[-q[0],-q[1],-q[2],q[3]]).slice(0,3);
const add = (a,b) => a.map((v,i)=>v+b[i]);
function accessor(index) {
  const a=data.accessors[index],v=data.bufferViews[a.bufferView];
  if(a.componentType!==5126 || a.sparse) throw Error('Expected dense float tracks');
  const width={SCALAR:1,VEC3:3,VEC4:4}[a.type],stride=v.byteStride||width*4;
  return Array.from({length:a.count},(_,i)=>Array.from({length:width},(_,k)=>binary.readFloatLE((v.byteOffset||0)+(a.byteOffset||0)+i*stride+k*4)));
}
const cache=new Map();const read=i=>cache.get(i)|| (cache.set(i,accessor(i)),cache.get(i));
const parent=[];data.nodes.forEach((n,i)=>(n.children||[]).forEach(c=>parent[c]=i));
const names=['DEF-hips','DEF-spine.001','DEF-spine.002','DEF-spine.003','DEF-neck','DEF-head','DEF-shoulder.L','DEF-upper_arm.L','DEF-forearm.L','DEF-hand.L','DEF-shoulder.R','DEF-upper_arm.R','DEF-forearm.R','DEF-hand.R','DEF-thigh.L','DEF-shin.L','DEF-foot.L','DEF-toe.L','DEF-thigh.R','DEF-shin.R','DEF-foot.R','DEF-toe.R'];
const indices=names.map(n=>data.nodes.findIndex(x=>x.name===n));
function pose(animation,time) {
  const local=data.nodes.map(n=>({p:n.translation||[0,0,0],q:n.rotation||[0,0,0,1]}));
  for(const c of animation.channels) {
    if(c.target.path==='scale') continue;
    const s=animation.samplers[c.sampler],ts=read(s.input),vs=read(s.output);
    let k=0;while(k+1<ts.length && ts[k+1][0]<=time)k++;
    let value=vs[k];
    if(s.interpolation!=='STEP' && k+1<ts.length){
      if(s.interpolation && s.interpolation!=='LINEAR') throw Error('Unsupported interpolation');
      const t=(time-ts[k][0])/(ts[k+1][0]-ts[k][0]);
      let next=vs[k+1];
      if(c.target.path==='rotation' && value.reduce((s,v,i)=>s+v*next[i],0)<0)next=next.map(v=>-v);
      value=value.map((v,i)=>v+(next[i]-v)*t);
      if(c.target.path==='rotation'){let n=Math.hypot(...value);value=value.map(v=>v/n);}
    }
    local[c.target.node][c.target.path==='rotation'?'q':'p']=value;
  }
  const world=[];
  function visit(i){if(world[i])return world[i];let n=local[i];if(parent[i]!==undefined){let p=visit(parent[i]);n={p:add(p.p,rotate(p.q,n.p)),q:mul(p.q,n.q)}}return world[i]=n;}
  // Reflect Z: left remains +X, toes face PMX -Z.
  return indices.map(i=>{const v=visit(i);return {p:{x:v.p[0],y:v.p[1],z:-v.p[2]},q:{x:-v.q[0],y:-v.q[1],z:v.q[2],w:v.q[3]}};});
}
const bind=pose(data.animations.find(a=>a.name==='A_TPose'),0);
const clips=['Idle_Loop','Walk_Loop'].map(name=>{
  const a=data.animations.find(x=>x.name===name),duration=Math.max(...a.samplers.map(s=>read(s.input).at(-1)[0]));
  const count=Math.ceil(duration*60),times=Array.from({length:count+1},(_,i)=>duration*i/count);
  const frames=times.map(t=>({joints:pose(a,t)}));
  // Distribute the source's tiny endpoint drift over the final 200ms.
  const first=frames[0].joints,last=frames.at(-1).joints;
  frames.forEach((frame,i)=>{
    const t=Math.max(0,(times[i]-(duration-.2))/.2),weight=t*t*(3-2*t);
    frame.joints.forEach((joint,k)=>{
      for(const axis of ['x','y','z']) joint.p[axis]+= (first[k].p[axis]-last[k].p[axis])*weight;
      const q=joint.q,a=first[k].q,b=last[k].q;
      const sign=a.x*b.x+a.y*b.y+a.z*b.z+a.w*b.w<0?-1:1;
      for(const axis of ['x','y','z','w'])q[axis]+=(a[axis]*sign-b[axis])*weight;
      const norm=Math.hypot(q.x,q.y,q.z,q.w);for(const axis of ['x','y','z','w'])q[axis]/=norm;
    });
  });
  return {name,duration,fps:(times.length-1)/duration,frames};
});
const result={source:'Quaternius Universal Animation Library Standard, CC0-1.0',names,bind,clips};
fs.mkdirSync('Assets/Resources/Characters',{recursive:true});
fs.writeFileSync('Assets/Resources/Characters/RemielleMotion.json',JSON.stringify(result));
console.log(JSON.stringify({bind:bind.map((b,i)=>({name:names[i],p:b.p})),clips:clips.map(c=>({name:c.name,frames:c.frames.length,duration:c.duration}))},null,2));
