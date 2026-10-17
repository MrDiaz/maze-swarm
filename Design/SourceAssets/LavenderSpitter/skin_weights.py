"""Measured semantic skin weights; UV copies share exact weights on the same surface point."""
def ss(a,b,x):
 t=max(0.,min(1.,(x-a)/(b-a)));return t*t*(3-2*t)
def limit(w):
 w=dict(sorted(((n,a) for n,a in w.items() if a>.000001),key=lambda q:(-q[1],q[0]))[:4])
 total=sum(w.values());return {n:a/total for n,a in w.items()}
def compute_weights(positions,edges,semantic):
 keys=[tuple(round(c,6) for c in q) for q in positions]
 weld={}
 for i,k in enumerate(keys):weld.setdefault(k,[]).append(i)
 cyan={k:max(semantic[i]['cyanBody'] for i in ids) for k,ids in weld.items()}
 bykey={}
 for k,ids in weld.items():
  x,y,z=positions[ids[0]];ax=abs(x);side='L' if x>=0 else 'R';c=cyan[k]
  leg=(1-ss(.18,.28,z))*((1-ss(.09,.15,z))+ss(.09,.15,z)*ss(.13,.24,ax)*(1-ss(.05,.17,y)))
  leg*=(1-c)*(1-ss(.145,.20,y))
  arm=ss(.265,.365,ax)*ss(.185,.255,z)*(1-ss(.455,.515,z))*(1-ss(.15,.27,y))*(1-leg)*(1-c)
  core=max(0,1-leg-arm)
  # Smooth spatial neck collar. Source paint must not create a discontinuous jaw/neck joint.
  head=max(ss(.47,.60,z),ss(.35,.43,z)*(1-ss(-.22,-.13,y)))
  spine=ss(.275,.39,z);foot=1-ss(.08,.13,z);thigh=ss(.12,.205,z);fore=ss(.325,.43,ax)
  bykey[k]=limit({'Head':core*head,'Spine':core*(1-head)*spine,'Hips':core*(1-head)*(1-spine),'Foot.'+side:leg*foot,'Thigh.'+side:leg*(1-foot)*thigh,'Shin.'+side:leg*(1-foot)*(1-thigh),'UpperArm.'+side:arm*(1-fore),'Forearm.'+side:arm*fore})
 adj={k:set() for k in weld}
 for a,b in edges:
  ka,kb=keys[a],keys[b]
  if ka!=kb:adj[ka].add(kb);adj[kb].add(ka)
 for iteration in range(3):
  old={k:dict(q) for k,q in bykey.items()}
  for k,ids in weld.items():
   x,y,z=positions[ids[0]]
   if z<.08 or z>.61 or cyan[k]>.5:continue
   ns=adj[k]
   if not ns:continue
   w={n:.65*a for n,a in old[k].items()}
   for q in ns:
    for n,a in old[q].items():w[n]=w.get(n,0)+.35*a/len(ns)
   bykey[k]=limit(w)
 return [dict(bykey[k]) for k in keys]
