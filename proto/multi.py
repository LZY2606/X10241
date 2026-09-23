import tune as T, math, itertools
THR=T.chi2_ppf(0.95,2)
def fe(means,Sms,Se,y,p):
    mu=[sum(p[i]*means[i][j] for i in range(3)) for j in range(2)]
    C=[row[:] for row in Se]
    for i,w in enumerate(p):
        for a in range(2):
            for b in range(2): C[a][b]+=w*Sms[i][a][b]
    r=[y[j]-mu[j] for j in range(2)]
    Ci=T.inv(C)
    return sum(r[a]*sum(Ci[a][b]*r[b] for b in range(2)) for a in range(2))<=THR

def run(means,Sms,Se,y,k=60,quiet=False):
    pts,iv=T.simplex_grid(3,k); flags=[fe(means,Sms,Se,y,p) for p in pts]
    comps=T.components_from_comps(iv,flags,3,k); cs=sorted(comps,key=lambda c:-len(c))
    if not quiet:
        print('feas=%d/%d ncomp=%d sizes=%s'%(sum(flags),len(flags),len(cs),[len(c) for c in cs[:5]]))
        for c in cs[:4]:
            cp=[pts[i] for i in c]
            print('   cen=[%.3f,%.3f,%.3f] p1[%.3f,%.3f]'%(
                sum(q[0] for q in cp)/len(cp),sum(q[1] for q in cp)/len(cp),sum(q[2] for q in cp)/len(cp),
                min(q[0] for q in cp),max(q[0] for q in cp)))
    return len(cs),[len(c) for c in cs]

# construction: A,B far apart horizontally, both big isotropic variance;
# C near y with tiny variance -> C vertex tight, but along AB middle the big
# pooled variance makes residual acceptable too; gap between neighborhoods
means=[[-20.0,0.0],[20.0,0.0],[1.0,0.0]]
big=[[64.0,0],[0,64.0]]; small=[[0.04,0],[0,0.04]]
Sms=[big,big,small]; Se=[[0.04,0],[0,0.04]]
for yv in [0.0,0.5,1.0,-0.5]:
    print('y=',yv); run(means,Sms,Se,[yv,0.0])
