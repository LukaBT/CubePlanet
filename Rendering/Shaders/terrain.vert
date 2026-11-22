layout(location=0)in ivec4 aPi;layout(location=1)in uvec2 aSU;layout(location=2)in vec4 aNn;layout(location=3)in uvec4 aMf;
uniform mat4 uVP;uniform vec3 uO,uC,uWdir;uniform float uWt,uWnd,uTime;out vec2 vUv;flat out float vL;out float vS,vO;out vec3 vN;out vec3 vP;out float vAO;flat out uint vF;
void main(){vec3 aP=vec3(aPi.xyz)*.00390625,aN=aNn.xyz;float aL=float(aPi.w),aS=float(aSU.x)*(1./120.);vec2 aUv=vec2(float(aSU.y&31u),float((aSU.y>>5)&31u))*.0625;
 vAO=float((aSU.y>>10)&3u);uint mf=aMf.w&63u;vF=mf;vec3 p=aP+uO;float sf=step(1.5,aS),oq=floor(max(aS-2.,0.)*.5)*sf;
 if(uWt>.5&&uWt<1.5&&sf>.5){vec3 wp=p+uC,up=normalize(wp);p+=up*waveSum(up).x*(oq*.125);}
 if(uWt<.5&&(mf&4u)!=0u){vec3 wq=p+uC,uq=normalize(wq),sq=normalize(cross(uq,vec3(.31,.83,.47)));float ph=dot(wq,vec3(.7,.4,.6))*.5;p+=(sq*sin(uTime*1.3+ph)+cross(uq,sq)*cos(uTime*1.1+ph*1.3))*.1*aUv.y*aUv.y;}
 if(uWt<.5&&uWnd>.01){float lf=float(mf&1u),pl=float((mf>>1)&1u);
  if(lf+pl>.5){vec3 wp=p+uC;float g=.5+.5*sin(dot(wp,uWdir)*.07-uTime*(1.2+3.*uWnd)),a=uWnd*(lf*.12+pl*.34*aUv.y*aUv.y);
   p+=(uWdir*(.35+.65*g)+cross(normalize(wp),uWdir)*sin(dot(wp,vec3(.9,.7,1.1))*.9+uTime*(3.+5.*uWnd))*.35)*a;}}
 vP=p;gl_Position=uVP*vec4(p,1.);vUv=aUv;vL=aL;vS=aS-2.*sf-2.*oq;vO=oq*.125;vN=aN;}