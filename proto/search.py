import tune as T, math, random
THR=T.chi2_ppf(0.95,2)
def fe(means,s2,Se,y,p):
    mu=[sum(p[i]*means[i][j] for i in range(3)) for j in range(2)]
    v0=Se[0][0]+p[0]*s2[0][0]+p[1]*s2[1][0]+p[2]*s2[2][0]
    v1=Se[1][1]+p[0]*s2[0][1]+p[1]*s2[1][1]+p[2]*s2[2][1]
    r0=y[0]-mu[0]; r1=y[1]-mu[1]
    return r0*r0/v0+r1*r1/v1<=THR
def run(means,s2,Se,y,k=48):
    pts,iv=T.simplex_grid(3,k); flags=[fe(means,s2,Se,y,p) for p in pts]
    comps=T.components_from_comps(iv,flags,3,k); cs=sorted(comps,key=lambda c:-len(c))
    return flags,cs,pts
random.seed(7)
hits=[]
for trial in range(4000):
    means=[[random.uniform(-15,15),random.uniform(-15,15)] for _ in range(3)]
    s2=[[random.choice([0.05,0.1,0.5,2.0,8.0,25.0]),random.choice([0.05,0.1,0.5,2.0,8.0,25.0])] for _ in range(3)]
    y=[random.uniform(-12,12),random.uniform(-12,12)]
    Se=[[0.05,0.0],[0.0,0.05]]
    flags,cs,pts=run(means,s2,Se,y)
    if len(cs)>=2 and len(cs[1])>=3:
        hits.append((means,s2,y,[len(c) for c in cs[:5]]))
        if len(hits)>=8: break
for h in hits:
    print(h[2],'sizes',h[3])
    print(' means',h[0]); print(' s2',h[1])
print('hits',len(hits))
