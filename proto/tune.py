import math, itertools, json

def mat(n,m,v=0.0): return [[v]*m for _ in range(n)]
def transpose(A): return [list(r) for r in zip(*A)]
def mm(A,B):
    n=len(A); k=len(B); m=len(B[0]); C=mat(n,m)
    for i in range(n):
        Ai=A[i]; Ci=C[i]
        for t in range(k):
            a=Ai[t]
            if a==0: continue
            Bt=B[t]
            for j in range(m): Ci[j]+=a*Bt[j]
    return C
def mv(A,v):
    return [sum(a*b for a,b in zip(row,v)) for row in A]
def madd(A,B,s=1.0):
    n=len(A);
    return [[A[i][j]+s*B[i][j] for j in range(len(A[0]))] for i in range(n)]
def inv(A):
    n=len(A); M=[list(map(float,A[i]))+[1.0 if i==j else 0.0 for j in range(n)] for i in range(n)]
    for col in range(n):
        piv=max(range(col,n),key=lambda r:abs(M[r][col]))
        M[col],M[piv]=M[piv],M[col]
        d=M[col][col]
        for j in range(2*n): M[col][j]/=d
        for r in range(n):
            if r==col: continue
            f=M[r][col]
            for j in range(2*n): M[r][j]-=f*M[col][j]
    return [row[n:] for row in M]

def chi2_ppf(p, k, it=100):
    # Wilson-Hilferty approximation then Newton refinement of lower regularized gamma
    # use WH as seed; refine with series/continued fraction (no scipy)
    t=1.0-2.0/(9*k); s=1.0 if p>0.5 else -1.0
    # normal quantile via Beasley-Springer-Moro approximation
    z=_nq(p)
    x=k*(t+z*math.sqrt(2.0/(9*k)))**3
    if x<=0: x=0.1
    for _ in range(it):
        c=_gammainc(k/2.0, x/2.0)-p
        d=math.exp(-x/2.0+(k/2.0-1)*math.log(x/2.0)-math.lgamma(k/2.0))/2.0
        if d<=0: break
        x-=c/d
        if x<=0: x=1e-6
    return x

def _nq(p):
    # Acklam approximation
    a=[-3.969683028665376e+01,2.209460984245205e+02,-2.759285104469687e+02,1.383577518672690e+02,-3.066479806614716e+01,2.506628277459239e+00]
    b=[-5.447609879822406e+01,1.615858368580409e+02,-1.556989798598866e+02,6.680131188771972e+01,-1.328068155288572e+01]
    c=[-7.784894002430293e-03,-3.223964580411365e-01,-2.400758277161838e+00,-2.549732539343734e+00,4.374664141464968e+00,2.938163982698783e+00]
    d=[7.784695709041462e-03,3.224671290700398e-01,2.445134137142996e+00,3.754408661907416e+00]
    plow=0.02425; phigh=1-plow
    if p<plow:
        q=math.sqrt(-2*math.log(p))
        return (((((c[0]*q+c[1])*q+c[2])*q+c[3])*q+c[4])*q+c[5])/((((d[0]*q+d[1])*q+d[2])*q+d[3])*q+1)
    if p<=phigh:
        q=p-0.5; r=q*q
        return (((((a[0]*r+a[1])*r+a[2])*r+a[3])*r+a[4])*r+a[5])*q/(((((b[0]*r+b[1])*r+b[2])*r+b[3])*r+b[4])*r+1)
    q=math.sqrt(-2*math.log(1-p))
    return -(((((c[0]*q+c[1])*q+c[2])*q+c[3])*q+c[4])*q+c[5])/((((d[0]*q+d[1])*q+d[2])*q+d[3])*q+1)

def _gammainc(a,x):
    if x<0: return 0.0
    if x<a+1:
        ap=a; s=1.0/a; d=s
        for _ in range(200):
            ap+=1; d*=x/ap; s+=d
            if abs(d)<abs(s)*1e-14: break
        return s*math.exp(-x+a*math.log(x)-math.lgamma(a))
    else:
        b=x+1-a; c=1e300; d=1.0/b; h=d
        for i in range(1,200):
            an=-i*(i-a)
            b+=2; d=an*d+b
            if abs(d)<1e-300: d=1e-300
            c=b+an/c
            if abs(c)<1e-300: c=1e-300
            d=1.0/d; delta=d*c; h*=delta
            if abs(delta-1)<1e-14: break
        return 1.0-math.exp(-x+a*math.log(x)-math.lgamma(a))*h

# simplex lattice points (n comps, subdivisions k): integer compositions
def simplex_grid(n,k):
    pts=[]; comps=[]
    def rec(start, rem, cur):
        if start==n-1:
            c=cur+[rem]; comps.append(tuple(c)); pts.append(tuple(v/k for v in c)); return
        for v in range(rem+1):
            rec(start+1, rem-v, cur+[v])
    rec(0,k,[])
    return pts, comps

def components_from_comps(comps, flags, n, k):
    idx={c:i for i,c in enumerate(comps)}
    seen=[False]*len(comps); out=[]
    for i,f in enumerate(flags):
        if f and not seen[i]:
            stack=[i]; seen[i]=True; members=[]
            while stack:
                u=stack.pop(); members.append(u); c=comps[u]
                for a in range(n):
                    for b in range(n):
                        if a==b or c[a]>=k or c[b]<=0: continue
                        nc=list(c); nc[a]+=1; nc[b]-=1
                        j=idx.get(tuple(nc))
                        if j is not None and flags[j] and not seen[j]:
                            seen[j]=True; stack.append(j)
            out.append(members)
    return out

def total_cov(p, Sm, Se, frac_extra=0.0):
    # Sm: source mean cov contributions per component (d x d), Se: measurement cov
    C=[row[:] for row in Se]
    for i,wi in enumerate(p):
        C=madd(C, Sm[i], wi)
    if frac_extra:
        C=madd(C, [[frac_extra]], 0) if len(C)==1 else C
    return C

def feasible(means, Sm, Se, y, p, level=0.95):
    d=len(y)
    mu=[sum(p[i]*means[i][j] for i in range(len(p))) for j in range(d)]
    C=total_cov(p, Sm, Se)
    r=[y[j]-mu[j] for j in range(d)]
    Ci=inv(C)
    t=sum(r[a]*sum(Ci[a][b]*r[b] for b in range(d)) for a in range(d))
    thr=chi2_ppf(level,d)
    return t<=thr, t, thr

def hull_contains(means, y):
    # d==1: interval; d==2: triangle (3 comps)
    n=len(means); d=len(y)
    if d==1:
        lo=min(m[0] for m in means); hi=max(m[0] for m in means)
        return lo-1e-9<=y[0]<=hi+1e-9
    if d==2 and n==3:
        A=means; signs=[]
        for i in range(3):
            j=(i+1)%3
            c=(A[j][0]-A[i][0])*(y[1]-A[i][1])-(A[j][1]-A[i][1])*(y[0]-A[i][0])
            signs.append(c)
        return all(s>=-1e-9 for s in signs) or all(s<=1e-9 for s in signs)
    return True

def analyze(name, means, Sm, Se, y, k=60, dims=None):
    n=len(means); d=len(y)
    pts, comps_iv = simplex_grid(n,k)
    flags=[]; tmax=0
    for p in pts:
        ok,t,thr=feasible(means,Sm,Se,y,p)
        flags.append(ok); tmax=max(tmax,t)
    comps=components_from_comps(comps_iv,flags,n,k)
    feas=[pts[i] for i,f in enumerate(flags) if f]
    info=dict(name=name, n=n, d=d, ngrid=len(pts), nfeas=len(feas),
              ncomp=len(comps), in_hull=hull_contains(means,y))
    if comps:
        cs=sorted(comps,key=lambda c:-len(c))
        info['comp_sizes']=[len(c) for c in cs]
        # component centers + extreme p1
        centers=[]
        for c in cs:
            cp=[pts[i] for i in c]
            cen=[sum(q[j] for q in cp)/len(cp) for j in range(n)]
            centers.append(dict(center=[round(v,3) for v in cen],
                                p1range=[round(min(q[0] for q in cp),3),round(max(q[0] for q in cp),3)]))
        info['comps']=centers
    info['max_t']=round(tmax,2)
    return info

if __name__=='__main__':
    # ---- 1 baseline: 3 sources, 2 isotopes, well-separated, interior ----
    means1=[[-26.0,4.0],[-12.0,12.0],[-20.0,-8.0]]
    Sm1=[[[0.25,0.02],[0.02,0.16]]]*3
    Se1=[[0.36,0.05],[0.05,0.25]]
    y1=[-19.0,3.0]
    r1=analyze('baseline',means1,Sm1,Se1,y1)
    # ---- 2 collinear: 3 sources on a line, 2 isotopes ----
    means2=[[-26.0,4.0],[-20.0,0.0],[-14.0,-4.0]]
    Sm2=[[[0.25,0],[0,0.25]]]*3
    Se2=[[0.36,0],[0,0.36]]
    y2=[-20.0,0.0]
    r2=analyze('collinear',means2,Sm2,Se2,y2)
    # ---- 3 outside hull ----
    y3=[-4.0,20.0]
    r3=analyze('outside',means1,Sm1,Se1,y3)
    # ---- 4 correlated errors (just inspect feasible width) ----
    Se4=[[0.36,0.16],[0.16,0.25]]
    r4=analyze('correlated',means1,Sm1,Se4,y1)
    # ---- 6 fractionation switch: same setup, f shifts effective means ----
    means6=[[m[0]-1.0,m[1]+0.5] for m in means1]
    r6=analyze('frac_shift',means6,Sm1,Se1,y1)
    # ---- 7 multiple components: 2 sources, 1 isotope, anisotropic inflated cov ----
    means7=[[-10.0],[10.0]]
    Sm7=[[[6.25]],[[6.25]]]
    Se7=[[0.04]]
    y7=[0.0]
    r7=analyze('multiregion',means7,Sm7,Se7,y7,k=200)
    # scan sigma to find min giving 2 comps
    for r in (r1,r2,r3,r4,r6,r7):
        print(r['name'], 'feas=%d/%d comps=%d sizes=%s hull=%s maxT=%s'%(r['nfeas'],r['ngrid'],r['ncomp'],r.get('comp_sizes'),r['in_hull'],r['max_t']))
        if r.get('comps'): print('   ', r['comps'][:3])
    for sig in [4.5,5.0,5.25,5.5,5.75,6.0]:
        rr=analyze(f'sig{sig}',means7,[[[sig*sig]]]*2,Se7,y7,k=300)
        print(sig, rr['ncomp'], rr['comp_sizes'] if rr['ncomp'] else None, rr.get('comps'))
