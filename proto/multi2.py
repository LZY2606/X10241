import tune as T, itertools
THR=T.chi2_ppf(0.95,2)
# each source: list of modes (mean, diagonal variances); combos cartesian product
def analyze(name, sources, Se, y, k=60):
    n=len(sources)
    combos=list(itertools.product(*[list(range(len(s))) for s in sources]))
    def fcombo(p, combo):
        mu=[0.0,0.0]; C=[row[:] for row in Se]
        for i,ki in enumerate(combo):
            m,s2=sources[i][ki]
            mu[0]+=p[i]*m[0]; mu[1]+=p[i]*m[1]
            C[0][0]+=p[i]*s2[0]; C[1][1]+=p[i]*s2[1]
        r0=y[0]-mu[0]; r1=y[1]-mu[1]
        return r0*r0/C[0][0]+r1*r1/C[1][1]
    pts,iv=T.simplex_grid(n,k)
    owner=[-1]*len(pts); flags=[False]*len(pts)
    for ci,combo in enumerate(combos):
        for j,p in enumerate(pts):
            if not flags[j] and fcombo(p,combo)<=THR:
                flags[j]=True; owner[j]=ci
    comps=T.components_from_comps(iv,flags,n,k); cs=sorted(comps,key=lambda c:-len(c))
    print(name,'feas=%d/%d ncomp=%d sizes=%s'%(sum(flags),len(flags),len(cs),[len(c) for c in cs[:6]]))
    for c in cs[:4]:
        cp=[pts[i] for i in c]
        print('   cen=[%.3f,%.3f,%.3f] p=[%.2f..%.2f,%.2f..%.2f]'%(
            sum(q[0] for q in cp)/len(cp),sum(q[1] for q in cp)/len(cp),sum(q[2] for q in cp)/len(cp),
            min(q[0] for q in cp),max(q[0] for q in cp),min(q[2] for q in cp),max(q[2] for q in cp)))
    return len(cs)

A=[((0.0,0.0),(0.16,0.16))]
B=[((10.0,0.0),(0.16,0.16))]
for c1y,c2y in [(6.0,15.0),(5.0,16.0),(4.0,15.0)]:
    C=[((5.0,c1y),(0.16,0.16)),((5.0,c2y),(0.16,0.16))]
    analyze(f'C {c1y}/{c2y}',[A,B,C],[[0.04,0.0],[0.0,0.04]],[5.0,3.0])
