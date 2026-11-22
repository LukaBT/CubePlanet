namespace CubePlanet;

// GLSL sources extracted verbatim from the original WebGL2 page (weather-only programs removed).
// Compiled for Vulkan by Gpu.Spirv: it prepends #version 450, one std140 uniform block holding every loose uniform below
// (the 'uniform ...;' lines are stripped), push constants (uO, uWt, uK) and the sampler bindings.
// Vulkan clip space: Y points down and depth is reverse-Z (1 = near plane, 0 = infinitely far).
public static class Glsl
{
    public const string WAVE = """

uniform float uWA,uWL,uWK,uWD,uWTm,uWR0,uSea;
// every wave is a ring wave on the sphere: phase = k * (great-circle distance from a fixed source) - w*t, so crests travel and the field is the same for everybody
vec4 wv1(vec3 up,vec3 S,float k0,float A,float pw){
 float c=dot(S,up),sn=length(cross(S,up)),th=atan(sn,c),k=min(k0/uWL,2.3);
 float ph=mod(k*uWR0*th-1.6*sqrt(k)*uWTm,6.2831853),u=.5+.5*sin(ph),a=A*uWA;vec3 e=(up*c-S)/max(sn,.02);
 return vec4(a*(2.*pow(u,pw)-1.),e*(a*pw*pow(u,pw-1.)*cos(ph)*k));}
vec3 wrot(vec3 s){float c=cos(uWD),n=sin(uWD);return normalize(vec3(s.x*c+s.z*n,s.y,-s.x*n+s.z*c));}
vec4 waveSwell(vec3 up){float pw=max(uWK,1.05)+uSea,g=.45+7.*uSea;
 return wv1(up,wrot(vec3(.3,.8,.5)),.13,.2*g,pw)+wv1(up,wrot(vec3(-.7,.2,.65)),.21,.14*g,pw)+wv1(up,wrot(vec3(.9,.1,-.4)),.34,.08*g,pw);}
vec4 waveSum(vec3 up){float pw=max(uWK,1.05)+.5*uSea,g=1.+1.6*uSea;
 return waveSwell(up)+g*(wv1(up,wrot(vec3(.5,-.6,.6)),.7,.06,pw)+wv1(up,wrot(vec3(-.4,-.7,-.55)),1.1,.04,pw)+wv1(up,wrot(vec3(-.1,.95,-.3)),1.6,.025,pw)+wv1(up,wrot(vec3(.6,.3,-.8)),.42,.05,pw));}
float waveMax(){return .75*max(uWA,.01)*(.34*(.45+7.*uSea)+.175*(1.+1.6*uSea));}
float gCalm=0.;
""";
    public const string COMMON_BODY = """

uniform float uWR,uWF,uWC;
uniform vec3 uS,uC,uWind;
uniform float uOvc,uStorm,uFlash,uSnow,uWet;
uniform float uR,uAT,uTime,uFog,uRc0,uRc1,uCov,uShOn,uCsOn;
uniform sampler3D uN;
float gAO=1.; // ambient occlusion of the current fragment (1 = open), set by the terrain shader
vec3 sunTint(float se){return mix(vec3(1.,.36,.1),vec3(1.,.95,.84),smoothstep(0.,.5,se));}
vec3 sunCol(float se){return sunTint(se)*smoothstep(-.07,.12,se);}
vec3 tmap(vec3 c){c*=.9;c=(c*(2.51*c+.03))/(c*(2.43*c+.59)+.14);return pow(clamp(c,0.,1.),vec3(1./2.2));}
// analytic sky radiance seen along d by a viewer whose local up is 'up' (sun direction s)
vec3 skyCol(vec3 d,vec3 up,vec3 s){
 float mu=dot(d,up),se=dot(s,up),sd=max(dot(d,s),0.);
 float day=smoothstep(-.14,.3,se),tw=1.-smoothstep(0.,.42,abs(se+.03)),k=pow(1.-clamp(mu,0.,1.),2.6);
 vec3 zen=mix(vec3(.004,.007,.02),vec3(.06,.18,.6),day),hor=mix(vec3(.012,.018,.04),vec3(.5,.68,.93),day);
 vec3 c=mix(zen,hor,k);
 c+=tw*k*(vec3(1.1,.4,.12)*(.2+.8*pow(sd,3.))+vec3(.16,.1,.26)*(1.-sd)*.5);
 float dl=smoothstep(-.1,.2,se);
 c+=dl*(pow(sd,18.)*vec3(1.,.8,.55)*.3+pow(sd,300.)*vec3(1.,.9,.75)*1.5);
 c*=mix(1.,.7,clamp(-mu*3.,0.,1.));
 c=mix(c,vec3(dot(c,vec3(.3,.5,.2)))*.75,uOvc*.85);c+=uFlash*vec3(.5,.55,.8)*(.3+.7*k);
 return c;}
vec3 skyFog(vec3 d,vec3 up){vec3 h=d-up*dot(d,up);float l=length(h);h=l>1e-4?h/l:d;return skyCol(normalize(mix(d,h,.75)),up,uS);}
// ---- volumetric clouds (uCloudN: R = Perlin-Worley shapes, GBA = Worley detail) ----
// weather = where clouds may form: two large, non-matching scales so the pattern never visibly repeats; shapes = Perlin-Worley
// limited by a height profile (flat bases, rounded tops, taller where coverage is dense), eroded by Worley detail
// (wispy at the bottom, billowy at the top). h = height inside the cloud layer, 0..1.
float cRemap(float v,float l0,float h0,float l1,float h1){return l1+(v-l0)*(h1-l1)/max(h0-l0,1e-4);}
float cWeather(vec3 p){float a=texture(uCloudN,p*(1./11000.)+uWindW).r,b=texture(uCloudN,p*(1./4300.)+vec3(.37,.11,.71)+uWindW*2.).g;
 return clamp((a*.7+b*.5-.62)*2.4+uCov*1.1,0.,1.);}
float cDens(vec3 p,float h,bool lite){
 float wc=cWeather(p);if(wc<.01)return 0.;
 float top=.3+.65*wc,prof=smoothstep(0.,.07,h)*(1.-smoothstep(top*.5,top,h));
 float sh=cRemap(texture(uCloudN,p*(1./850.)+uWind).r*prof,1.-wc,1.,0.,1.);
 if(sh<=0.||lite)return smoothstep(0.,.3,sh);
 vec3 dt=texture(uCloudN,p*(1./150.)+uWindD).gba;float det=dt.x*.625+dt.y*.25+dt.z*.125;
 return smoothstep(0.,.3,cRemap(sh,mix(1.-det,det,clamp(h*3.,0.,1.))*.5,1.,0.,1.));} // steep ramp = crisp edges
// cloud shadow on the ground: density where the sun ray crosses the lower part of the layer
float cloudSh(vec3 wp){if(uCloudOn<.5)return 1.;float rm=uRc0+(uRc1-uRc0)*.3,b=dot(wp,uS),c=dot(wp,wp)-rm*rm,h=b*b-c;if(h<0.)return 1.;
 float t=-b+sqrt(h);if(t<0.)return 1.;return 1.-.8*smoothstep(0.,.35,cDens(wp+uS*t,.3,true));}
// direct sun + moon + hemispheric sky ambient + a dim head-torch in caves
vec3 lightIt(vec3 alb,vec3 n,vec3 up,vec3 wp,float sky,float sh,float dist){
 float se=dot(uS,up),dayF=smoothstep(-.16,.3,se),tw=1.-smoothstep(0.,.42,abs(se+.03));
 vec3 dir=sunCol(se)*2.7*max(dot(n,uS),0.)*sh*cloudSh(wp)*(1.-.8*uOvc);
 vec3 ambS=mix(vec3(.05,.07,.15),vec3(.5,.62,.9),dayF)+vec3(.3,.13,.06)*tw*.3;
 vec3 ambG=mix(vec3(.03,.035,.06),vec3(.3,.27,.22),dayF);
 vec3 amb=mix(ambG,ambS,dot(n,up)*.5+.5)*mix(.08,1.,sky)*gAO;dir*=.55+.45*gAO;
 vec3 moon=vec3(.12,.16,.3)*max(dot(n,-uS),0.)*(1.-dayF)*sky;
 vec3 lamp=vec3(0.);
 return alb*(dir+amb*(1.-.3*uOvc)+moon+lamp+uFlash*vec3(.9,.95,1.2)*sky);}
// sun shadow: up to 3 cascades (uShN), the nearest one that contains the point; 5x5 PCF in the near cascade, 3x3 further out
float shadowAt(vec3 p,vec3 n){
 if(uShOn<.5)return 1.;float nl=dot(n,uS);if(nl<=0.)return 1.;
 float off=.3+.5*(1.-nl),L=0.;vec4 sc=uLV*vec4(p+n*off,1.);
 if(max(abs(sc.x),abs(sc.y))>.96&&uShN>1.5){L=1.;sc=uLV1*vec4(p+n*off*2.5,1.);
  if(max(abs(sc.x),abs(sc.y))>.96&&uShN>2.5){L=2.;sc=uLV2*vec4(p+n*off*6.,1.);}}
 vec3 q=sc.xyz*.5+.5;if(q.z>=1.||q.z<=0.)return 1.;
 q.z-=.0006+.0016*(1.-nl);float s=0.,ws=0.;
 if(L<.5){for(int i=-2;i<=2;i++)for(int j=-2;j<=2;j++){float w=exp(-float(i*i+j*j)*.18);s+=w*texture(uSh,vec4(q.xy+vec2(float(i),float(j))*uShT*1.25,L,q.z));ws+=w;}}
 else{for(int i=-1;i<=1;i++)for(int j=-1;j<=1;j++){s+=texture(uSh,vec4(q.xy+vec2(float(i),float(j))*uShT*1.2,L,q.z));ws+=1.;}}
 float last=L>uShN-1.5?1.:0.;
 return mix(s/ws,1.,last*smoothstep(.8,1.,max(abs(sc.x),abs(sc.y))));}
// inverse of tmap(): turns a stored (tone-mapped, gamma) colour back into linear light so reflections blend like the sky term they replace
vec3 itm(vec3 y){y=pow(clamp(y,0.,.995),vec3(2.2));vec3 a=2.51-2.43*y,b=.03-.59*y,c=-.14*y;return (-b+sqrt(b*b-4.*a*c))/(2.*a)/.9;}
// screen-space reflection: march the reflected ray and look it up in the previous frame (colour + view distance), reprojected with last frame's camera
vec3 reflCol(vec3 wp,vec3 Rd,vec3 up,float dist){
 vec3 sky=skyCol(Rd,up,uS);
 if(uSsr<.5||dist>420.)return sky;
 vec3 r0=wp-uPC;float t=.35,dt=.5;
 for(int i=0;i<28;i++){
  vec3 rl=r0+Rd*t;vec4 cp=uPVP*vec4(rl,1.);if(cp.w<=.05)break;
  vec2 uv=cp.xy/cp.w*.5+.5;if(uv.x<0.||uv.y<0.||uv.x>1.||uv.y>1.)break;
  float sd=texture(uHistD,uv).r,pd=length(rl);
  if(pd>sd+.08&&pd<sd+dt*1.6+.6){
   vec2 e=smoothstep(0.,.07,uv)*smoothstep(0.,.07,1.-uv);vec4 h=texture(uHist,uv);
   return mix(sky,itm(h.rgb),e.x*e.y*h.a*(1.-smoothstep(260.,420.,dist))*(1.-smoothstep(.85,1.,float(i)/28.)));}
  t+=dt;dt*=1.2;}
 return sky;}
vec3 wxAlb(vec3 a,vec3 n,vec3 up,float sky){float t=smoothstep(.55,.9,dot(n,up))*smoothstep(.5,.9,sky);a=mix(a,vec3(.8,.85,.9),uSnow*t);return a*(1.-.35*uWet*(1.-uSnow*t));}
vec3 waterShade(vec3 wp,vec3 V,vec3 n,vec3 up,vec3 alb,float dist,float dep,out float al){
 vec3 ng=n,t1=normalize(cross(up,vec3(.31,.83,.47))),t2=cross(up,t1);vec2 p=vec2(dot(wp,vec3(.62,.21,-.75)),dot(wp,vec3(-.35,.9,.27)));
 float T=uWTm,lod=1.-smoothstep(40.,220.,dist),lw=1.-smoothstep(150.,520.,dist),topF=max(dot(ng,up),0.);
 vec4 wvv=waveSum(up)*(1.-gCalm);
 vec2 g=vec2(1.,.3)*(1.7*.6*cos(1.7*dot(vec2(1.,.3),p)+T*1.3))+vec2(-.4,1.)*(2.3*.5*cos(2.3*dot(vec2(-.4,1.),p)-T*1.1))
  +vec2(.8,-.7)*(3.1*.3*cos(3.1*dot(vec2(.8,-.7),p)+T*1.7))+vec2(-.9,-.4)*(4.7*.2*cos(4.7*dot(vec2(-.9,-.4),p)-T*2.1));
 vec3 q=wp*.09+vec3(T*.011,T*.007,T*.009);float h0=texture(uN,q).r;g+=vec2(texture(uN,q+t1*.02).r-h0,texture(uN,q+t2*.02).r-h0)*17.;
 vec3 q2=wp*.4+vec3(-T*.03,T*.02,T*.025);float h1=texture(uN,q2).r;g+=vec2(texture(uN,q2+t1*.02).r-h1,texture(uN,q2+t2*.02).r-h1)*10.;
 vec3 nw=normalize(ng-wvv.yzw*lw*topF-(t1*g.x+t2*g.y)*.03*lod*uWR*(1.+1.5*uSea)*topF);
 bool under=dot(-V,ng)<0.;if(under)nw=-nw;
 float cs=max(dot(-V,nw),0.);
 float se=dot(uS,up),dayF=smoothstep(-.16,.3,se),dd=smoothstep(.04,.9,dep);
 vec3 wc=mix(vec3(.04,.46,.47),vec3(.01,.06,.2),dd);wc=mix(wc,alb*.7,.25);wc*=1.-.4*uStorm;
 vec3 body=lightIt(wc,nw,up,wp,1.,1.,dist)*.85+wc*.12*dayF*(1.-dd);
 float foam=0.;
 if(topF>.9){
  foam=smoothstep(.76,.9,texture(uN,wp*.45+vec3(T*.02,-T*.015,0.)).r)*(1.-smoothstep(.1,.2,dep))*.6;
  float cr=smoothstep(.55,.9,wvv.x/waveMax())*smoothstep(.4,.7,texture(uN,wp*.3+vec3(T*.03,0.,T*.02)).r)*.6;
  foam=max(foam,cr)*lod*uWF*(1.+1.2*uSea);
  body=mix(body,lightIt(vec3(.9,.95,1.),nw,up,wp,1.,1.,dist),clamp(foam,0.,1.));}
 if(under){vec3 rf=refract(V,nw,1.333);vec3 od=dot(rf,rf)>.01?normalize(rf):reflect(V,nw);float win=smoothstep(.62,.72,cs);
  al=.9;return mix(body*.45,skyCol(od,up,uS)*vec3(.7,.9,1.)*.9,win);}
 float fr=.06+.94*pow(1.-clamp(cs,0.,1.),4.5);
 vec3 R=reflect(V,nw);R-=2.*min(dot(R,up),0.)*up;
 float rs=max(dot(R,uS),0.);vec3 spec=sunCol(se)*(pow(rs,320.)*16.+pow(rs,40.)*.3);
 al=max(mix(.5+.45*dd,.98,fr),clamp(foam,0.,1.));al=clamp(al*uWC,.1,1.);
 return mix(body,reflCol(wp,R,up,dist),fr)+spec;}
// glass panes and ice: sky reflection with fresnel, sun glints, frosted veins in ice
vec3 glassIce(vec3 wp,vec3 V,vec3 n,vec3 up,vec4 c,float L,float dist,float sky,out float al){
 float cs=abs(dot(-V,n)),fr=.05+.95*pow(1.-cs,4.);
 vec3 R=reflect(V,n);R-=2.*min(dot(R,up),0.)*up;
 float se=dot(uS,up),rs=max(dot(R,uS),0.),dayF=smoothstep(-.16,.3,se),lodf=1.-smoothstep(80.,300.,dist);
 vec3 sc=sunCol(se),alb=pow(c.rgb,vec3(2.2)),refl=skyCol(R,up,uS)*(.2+.8*sky);
 if(L<10.){
  float nz=texture(uN,wp*.035).r,nv=texture(uN,wp*.11+.3).r;
  float crack=(1.-smoothstep(0.,.03,abs(nv-.5)))*lodf,frost=smoothstep(.55,.8,texture(uN,wp*.5).r)*lodf;
  vec3 base=alb*mix(.8,1.15,nz);base=mix(base,vec3(.9,.95,1.),frost*.45+crack*.5);
  vec3 col=lightIt(base,n,up,wp,sky,1.,dist)+base*(.18+.2*dayF)*sky;
  col=mix(col,refl,fr*.85)+sc*(pow(rs,90.)*3.+pow(rs,12.)*.12)*(1.-.5*crack);
  al=min(mix(.7,.95,fr)+crack*.15+frost*.08,.98);return col;}
 float fm=smoothstep(.8,1.,c.a);
 vec3 body=lightIt(alb,n,up,wp,sky,1.,dist),col=mix(body,refl,fr*(1.-fm));
 col+=sc*pow(rs,140.)*6.*(1.-fm);
 al=max(c.a,mix(.07,.7,fr));al=mix(al,.95,fm);return col;}

""";
    public const string RAY = """
uniform vec3 uF,uRt,uUp;uniform float uTan,uAsp;in vec2 vQ;
vec3 viewRay(){return normalize(uF+uRt*(vQ.x*uTan*uAsp)+uUp*(vQ.y*uTan));}

""";
    public const string FSV = """
out vec2 vQ;void main(){vec2 p=vec2(float((gl_VertexID<<1)&2),float(gl_VertexID&2));vQ=p*2.-1.;gl_Position=vec4(vQ.x,-vQ.y,0.,1.);}
""";
    public const string PV_VS = """
layout(location=0)in ivec4 aPi;layout(location=1)in uvec2 aSU;layout(location=2)in vec4 aNn;
uniform mat4 uVP;uniform vec3 uO,uC,uWdir;uniform float uWt,uWnd,uTime;out vec2 vUv;flat out float vL;out float vS,vO;out vec3 vN;out vec3 vP;out float vAO;
void main(){vec3 aP=vec3(aPi.xyz)*.00390625,aN=aNn.xyz;float aL=float(aPi.w),aS=float(aSU.x)*(1./120.);vec2 aUv=vec2(float(aSU.y&1u),float((aSU.y>>1)&1u));
 vAO=float((aSU.y>>2)&3u);vec3 p=aP+uO;float sf=step(1.5,aS),oq=floor(max(aS-2.,0.)*.5)*sf;
 if(uWt>.5&&uWt<1.5&&sf>.5){vec3 wp=p+uC,up=normalize(wp);p+=up*waveSum(up).x*(oq*.125);}
 if(uWt<.5&&aL>109.5&&aL<117.5){vec3 wq=p+uC,uq=normalize(wq),sq=normalize(cross(uq,vec3(.31,.83,.47)));float ph=dot(wq,vec3(.7,.4,.6))*.5;p+=(sq*sin(uTime*1.3+ph)+cross(uq,sq)*cos(uTime*1.1+ph*1.3))*.1*aUv.y*aUv.y;}
 if(uWt<.5&&uWnd>.01){float L=aL,lf=step(4.5,L)*step(L,5.5)+step(22.5,L)*step(L,26.5)+step(50.5,L)*step(L,51.5)+step(93.5,L)*step(L,98.5),pl=step(42.5,L)*step(L,50.5)+step(54.5,L)*step(L,55.5)+step(103.5,L)*step(L,109.5);
  if(lf+pl>.5){vec3 wp=p+uC;float g=.5+.5*sin(dot(wp,uWdir)*.07-uTime*(1.2+3.*uWnd)),a=uWnd*(lf*.12+pl*.34*aUv.y*aUv.y);
   p+=(uWdir*(.35+.65*g)+cross(normalize(wp),uWdir)*sin(dot(wp,vec3(.9,.7,1.1))*.9+uTime*(3.+5.*uWnd))*.35)*a;}}
 vP=p;gl_Position=uVP*vec4(p,1.);vUv=aUv;vL=aL;vS=aS-2.*sf-2.*oq;vO=oq*.125;vN=aN;}
""";
    public const string PV_VS_PREFIX = "WAVE+";
    public const string PV_FS = """
in vec2 vUv;flat in float vL;in float vS,vO;in vec3 vN;in vec3 vP;in float vAO;
out vec4 o;layout(location=1) out float oD;
void main(){vec4 c=texture(uT,vec3(vUv,vL));if(uWt<.5&&c.a<.5)discard;
 vec3 alb=pow(c.rgb,vec3(2.2)),n=normalize(vN),wp=vP+uC,up=normalize(wp),col;float dist=length(vP),al=1.;vec3 V=vP/dist;if(uWt<.5)alb=wxAlb(alb,n,up,vS);
 gCalm=1.-vO;if(uWt>1.5)col=glassIce(wp,V,n,up,c,vL,dist,vS,al);else if(uWt>.5)col=waterShade(wp,V,n,up,alb,dist,vS,al);else{float a=vAO/3.;gAO=mix(.38,1.,a*a*(3.-2.*a));col=lightIt(alb,n,up,wp,vS,min(shadowAt(vP,n),smoothstep(.55,.95,vS)),dist);}
 col=mix(col,skyFog(V,up),(1.-exp(-dist*uFog))*mix(.1,1.,(uWt>.5&&uWt<1.5)?1.:vS));o=vec4(tmap(col),al);oD=dist;
 if(uWt<.5&&(abs(vL-75.)<.5||abs(vL-85.)<.5||abs(vL-88.)<.5))o.rgb+=c.rgb*c.rgb*1.3*exp(-dist*uFog);} // lava / magma / sea lanterns glow (picked up by bloom)
""";
    public const string PV_FS_PREFIX = "";
    public const bool PV_COMMON = true;
    public const string PS_VS = """
layout(location=0)in ivec4 aPi;layout(location=1)in uvec2 aSU;uniform mat4 uLV;uniform vec3 uO;out vec2 vUv;flat out float vL;
void main(){vec3 aP=vec3(aPi.xyz)*.00390625;float aL=float(aPi.w);vec2 aUv=vec2(float(aSU.y&1u),float((aSU.y>>1)&1u));mat4 LM=uWt<.5?uLV:uWt<1.5?uLV1:uLV2;gl_Position=LM*vec4(aP+uO,1.);gl_Position.z=gl_Position.z*.5+.5*gl_Position.w;vUv=aUv;vL=aL;}
""";
    public const string PS_VS_PREFIX = "";
    public const string PS_FS = """
in vec2 vUv;flat in float vL;uniform sampler2DArray uT;void main(){if((vL>42.5&&vL<55.5)||(vL>103.5&&vL<117.5))discard;if(texture(uT,vec3(vUv,vL)).a<.5)discard;}
""";
    public const string PS_FS_PREFIX = "";
    public const bool PS_COMMON = false;
    public const string PL_VS = """
layout(location=0)in vec3 aP;layout(location=1)in vec2 aA;layout(location=2)in vec3 aC;layout(location=3)in float aI;layout(location=4)in vec3 aN;
uniform mat4 uVP;uniform vec3 uO;out vec2 vA;out vec3 vC;out vec3 vP;out vec3 vN;
// distant terrain sits a little behind the voxel terrain: a proportional depth offset (reverse-Z: smaller = farther). A slope-scaled
// depth bias pushed steep skirts and horizon patches all the way to the far plane, where the sky was drawn over them (white seams).
void main(){vP=aP+uO;gl_Position=uVP*vec4(vP,1.);gl_Position.z*=.9985;vA=aA;vC=aC;vN=aN;}
""";
    public const string PL_VS_PREFIX = "";
    public const string PL_FS = """
in vec2 vA;in vec3 vC;in vec3 vP;in vec3 vN;uniform sampler2DArray uT;uniform float uD,uNear;out vec4 o;layout(location=1) out float oD;
void main(){if(length(vP)<uNear)discard;float g=texture(uT,vec3(vA/uD,2.)).r*2.04;vec3 alb=pow(vC*(.6+.4*g),vec3(2.2)),n=normalize(vN),wp=vP+uC,up=normalize(wp);float dist=length(vP);vec3 V=vP/dist;alb=wxAlb(alb,n,up,1.);
 vec3 col=lightIt(alb,n,up,wp,1.,shadowAt(vP,n),dist);col=mix(col,skyFog(V,up),1.-exp(-dist*uFog));o=vec4(tmap(col),1.);oD=dist;}
""";
    public const string PL_FS_PREFIX = "";
    public const bool PL_COMMON = true;
    public const string PW_VS = """
layout(location=0)in vec3 aP;layout(location=3)in float aI;uniform mat4 uVP;uniform vec3 uO,uC;uniform float uRW;out float vI;out vec3 vQ;
void main(){vec3 q=aP+uO+uC;vec3 up=normalize(q);float sk=floor(aI*.25)*4.;q=up*(uRW-sk+waveSwell(up).x*(1.-smoothstep(250.,1200.,length(aP+uO))))-uC;vQ=q;gl_Position=uVP*vec4(q,1.);gl_Position.z*=.9985;vI=aI-sk;}
""";
    public const string PW_VS_PREFIX = "WAVE+";
    public const string PW_FS = """
in float vI;in vec3 vQ;uniform float uNear;out vec4 o;
void main(){if(length(vQ)<uNear)discard;vec3 wp=vQ+uC,up=normalize(wp);float dist=length(vQ),al;vec3 V=vQ/dist;
 vec3 col=waterShade(wp,V,up,up,pow(vec3(.17,.36,.78),vec3(2.2)),dist,.85,al);
 if(vI>.5)col=glassIce(wp,V,up,up,vec4(.62,.78,.95,1.),6.,dist,1.,al);
 col=mix(col,skyFog(V,up),1.-exp(-dist*uFog));o=vec4(tmap(col),al);}
""";
    public const string PW_FS_PREFIX = "";
    public const bool PW_COMMON = true;
    public const string PK_VS_PREFIX = "FSV";
    public const string PK_FS = """
out vec4 o;layout(location=1) out float oD;
float h13(vec3 p){p=fract(p*.1031);p+=dot(p,p.zyx+31.32);return fract((p.x+p.y)*p.z);}
void main(){vec3 d=viewRay();float r=length(uC);vec3 up=uC/r;float alt=max(r-uR,0.);
 float dens=1.-smoothstep(.15*uAT,uAT,alt),se=dot(uS,up),mu=dot(d,up);
 vec3 col=skyCol(d,up,uS)*dens;
 vec3 sp=d*170.,ip=floor(sp),fp=fract(sp)-.5;float hv=h13(ip);
 float star=step(.985,hv)*(1.-smoothstep(.08,.3,length(fp)))*(.55+.45*sin(uTime*2.5+hv*80.));
 float sv=mix(1.,1.-smoothstep(-.14,.12,se),dens)*mix(1.,smoothstep(-.03,.2,mu),dens);
 col+=star*(1.-uOvc)*vec3(.8,.9,1.)*2.2*sv*(.4+.6*fract(hv*91.));
 float nf=1.-smoothstep(-.12,.1,se);
 if(nf>.01&&uOvc<.97){vec3 ac=vec3(0.);float ro2=dot(uC,uC),bb=dot(uC,d),jt=fract(52.9829189*fract(dot(gl_FragCoord.xy,vec2(.06711056,.00583715)))),tt=uTime;
  for(int i=0;i<40;i++){float f=(float(i)+jt)/40.,rr=uR+uAT*(.1+.32*f),c=ro2-rr*rr,h=bb*bb-c;if(h<0.)continue;float sq=sqrt(h),t=c>0.?-bb-sq:-bb+sq;if(t<0.)continue;
   vec3 pn=normalize(uC+d*t);float lon=atan(pn.z,pn.x)*(pn.y>0.?1.:-1.),lat=abs(asin(clamp(pn.y,-1.,1.)));
   float fold=.05*sin(lon*3.+tt*.04)+.025*sin(lon*7.-tt*.07+1.)+.01*sin(lon*17.+tt*.15);
   float c1=1.22+fold,c2=1.0+.7*fold+.03*sin(lon*5.+2.+tt*.05),w=.02;
   float cur=exp(-pow((lat-c1)/w,2.))+.6*exp(-pow((lat-c2)/(w*1.3),2.));
   if(cur<.02)continue;
   float lt=lon/6.2832,ray=smoothstep(.4,.64,texture(uN,vec3(lt*45.,.3,tt*.004)).r*.6+texture(uN,vec3(lt*13.,.6,tt*.002)).r*.4),
    pulse=.65+.35*sin(tt*.35+lon*4.),
    vp=smoothstep(0.,.12,f)*exp(-f*4.5);
   vec3 col2=mix(vec3(.15,1.,.45),vec3(.9,.12,.3),smoothstep(.45,1.,f));col2=mix(vec3(.5,.25,.9),col2,smoothstep(0.,.1,f));
   ac+=col2*cur*ray*pulse*vp*.2;}
  col+=ac*nf*(1.-uOvc)*1.3;}
 float sd=dot(d,uS);
 col+=smoothstep(.99945,.99963,sd)*mix(vec3(1.,.96,.9),sunTint(se),dens)*30.*(1.-.97*uOvc);
 float md=dot(d,-uS);vec3 mt=vec3(.78,.83,.95);
 col+=smoothstep(.99962,.99972,md)*mt*mix(2.,2.8,1.-smoothstep(-.1,.2,se))*(.55+.45*h13(floor(d*60.)));
 col+=pow(max(md,0.),600.)*vec3(.3,.4,.6)*.5*(1.-smoothstep(-.1,.2,se));
 o=vec4(tmap(col)+(fract(sin(dot(gl_FragCoord.xy,vec2(12.9898,78.233)))*43758.5453)-.5)/255.,0.);oD=60000.;
 o.rgb+=vec3(1.,.86,.62)*smoothstep(.9992,.99963,sd)*2.2*(1.-.97*uOvc)*smoothstep(-.1,.05,se);}
""";
    public const string PK_FS_PREFIX = "RAY+";
    public const bool PK_COMMON = true;
    public const string PC_VS_PREFIX = "FSV";
    // half-resolution cloud raymarch (own pass after the scene; stops at terrain using the scene's distance buffer)
    public const string CLOUD_FS = """
out vec4 o;layout(location=1) out float oD;
vec2 rs(vec3 ro,vec3 rd,float rad){float b=dot(ro,rd),c=dot(ro,ro)-rad*rad,h=b*b-c;if(h<0.)return vec2(-1.);h=sqrt(h);return vec2(-b-h,-b+h);}
float ign(vec2 p){return fract(52.9829189*fract(dot(p,vec2(.06711056,.00583715))));}
float hg(float c,float g){float g2=g*g;return(1.-g2)/pow(1.+g2-2.*g*c,1.5)*.0795775;}
void main(){
 vec2 uv=vec2(vQ.x*.5+.5,.5-.5*vQ.y);vec3 d=viewRay(),ro=uC;float r=length(ro);
 vec4 g4=textureGather(uSrc,uv,0);float sd=max(max(g4.x,g4.y),max(g4.z,g4.w)); // farthest scene point under this half-res pixel
 o=vec4(0.);oD=60000.;
 vec2 a=rs(ro,d,uRc1),b=rs(ro,d,uRc0);float t0,t1;
 if(a.y<0.)return;
 if(r<uRc0){if(rs(ro,d,uR).x>0.)return;t0=b.y;t1=a.y;}
 else if(r<uRc1){t0=0.;t1=b.x>0.?b.x:a.y;}
 else{if(a.x<0.)return;t0=a.x;t1=b.x>t0?b.x:a.y;}
 float th=uRc1-uRc0;t1=min(t1,t0+th*(r>uRc1?3.:12.));t1=min(t1,sd); // grazing rays: the first ~12 layer-thicknesses decide what you see
 float far=th*70.;if(t1<=t0||t0>far)return;
 float len=t1-t0,N=uCloudQ,jt=fract(sin(dot(gl_FragCoord.xy,vec2(12.9898,78.233)))*43758.5453),T=1.,tHit=-1.;vec3 L=vec3(0.);
 float cosT=dot(d,uS),ph=mix(hg(cosT,.6),hg(cosT,-.25),.35)*12.566; // two-lobe phase: bright silver lining toward the sun
 int NL=uCloudQ>50.?6:uCloudQ>30.?5:3;
 for(int i=0;i<64;i++){if(float(i)>=N||T<.02)break;
  // steps grow with distance: detail up close, still reaching far clouds
  float x0=(float(i)+jt)/N,x1=min(1.,(float(i)+1.+jt)/N),ta=t0+len*pow(x0,1.6),dt=len*(pow(x1,1.6)-pow(x0,1.6));if(dt<=0.)break;
  vec3 p=ro+d*(ta+.5*dt);float pr=length(p),h=(pr-uRc0)/th;
  float dn=cDens(p,h,false);
  if(dn>.003){if(tHit<0.)tHit=ta;
   vec3 pu=p/pr;float se=dot(uS,pu);
   float ld=0.,ls=th*.04;vec3 q=p;
   for(int k=0;k<6;k++){if(k>=NL)break;q+=uS*ls;float hq=(length(q)-uRc0)/th;if(hq>1.||hq<0.)break;ld+=cDens(q,hq,k>2)*ls;ls*=1.7;}
   const float sig=.13;
   float beer=exp(-ld*sig*1.8),pw=1.-exp(-ld*sig*3.),ms=exp(-ld*sig*.3)*.4; // Beer-Lambert, powder (dark edges facing away), cheap multiple scattering
   float dayF=smoothstep(-.16,.3,se);
   vec3 amb=mix(vec3(.03,.04,.08),vec3(.5,.6,.85),dayF)*(.25+.75*h*h);
   vec3 lc=sunCol(se)*2.7*(beer*mix(1.,pw*2.,.5)*ph+ms)*(1.-.5*uStorm)+amb*(1.-.45*uStorm);
   float tr=exp(-dn*sig*dt);L+=T*lc*(1.-tr);T*=tr;}
 }
 float al=1.-T;if(al<.003)return;
 if(r>uRc1)al*=smoothstep(.4,.75,abs(dot(d,normalize(ro+d*t0))));
 vec3 c=L/max(1.-T,1e-3);float tf=tHit>0.?tHit:t0;
 float hz=max(1.-exp(-tf*uCF*.35),smoothstep(far*.25,far,tf));c=mix(c,skyFog(d,ro/r),hz);al*=1.-.9*hz*hz; // fade into the haze with distance
 o=vec4(tmap(c),al);oD=tf;}
""";
    public const string PG_VS_PREFIX = "FSV";
    public const string PG_FS = """
out vec4 o;
void main(){vec3 d=viewRay();float r=length(uC),alt=r-uR,w=smoothstep(.3*uAT,.9*uAT,alt);if(w<=0.)discard;
 float tc=max(-dot(uC,d),0.);vec3 P=uC+d*tc;float b=length(P),x=(b-uR)/uAT;if(x>1.)discard;
 float g=x>=0.?pow(1.-x,6.):exp(x*20.);float nd=dot(P/b,uS),lit=smoothstep(-.35,.4,nd),tw=1.-smoothstep(0.,.5,abs(nd));
 o=vec4(mix(vec3(.22,.48,1.),vec3(1.,.45,.18),tw*.8)*lit*g*w*1.1,1.);}
""";
    public const string PG_FS_PREFIX = "RAY+";
    public const bool PG_COMMON = true;
    public const string LS_VS = """
layout(location=0)in vec3 aP;void main(){mat4 LM=uWt<1.5?uLV1:uLV2;gl_Position=LM*vec4(aP+uO,1.);gl_Position.z=gl_Position.z*.5+.5*gl_Position.w;}
""";
    public const string LS_FS = """
void main(){}
""";
    // ---------- post-processing (fullscreen triangle from FSV; uO.xy = source texel size) ----------
    public const string POST_COMMON = """
in vec2 vQ;out vec4 o;
vec2 uvOf(){return vec2(vQ.x*.5+.5,.5-.5*vQ.y);}
vec3 tx(sampler2D s,vec2 uv){return min(texture(s,uv).rgb,vec3(16.));}
// half-res clouds: hidden where the scene is closer than the cloud
vec4 cloudAt(vec2 uv){if(uCloudOn<.5)return vec4(0.);
 // 4 bilinear taps one half-res texel apart: a small blur that hides the raymarch jitter when upscaling
 vec2 t=vec2(1.)/vec2(textureSize(uSrc3,0));
 vec4 cl=(texture(uSrc3,uv+t*vec2(-.5,-.5))+texture(uSrc3,uv+t*vec2(.5,-.5))+texture(uSrc3,uv+t*vec2(-.5,.5))+texture(uSrc3,uv+t*vec2(.5,.5)))*.25;
 return texture(uSrc5,uv).r+1.>texture(uSrc4,uv).r?cl:vec4(0.);}
// 13-tap downsample (box of boxes): smooth, no shimmering
vec3 d13(sampler2D s,vec2 uv,vec2 t){
 vec3 a=tx(s,uv+t*vec2(-2,2)),b=tx(s,uv+t*vec2(0,2)),c=tx(s,uv+t*vec2(2,2)),d=tx(s,uv+t*vec2(-2,0)),e=tx(s,uv),f=tx(s,uv+t*vec2(2,0)),
  g=tx(s,uv+t*vec2(-2,-2)),h=tx(s,uv+t*vec2(0,-2)),i=tx(s,uv+t*vec2(2,-2)),j=tx(s,uv+t*vec2(-1,1)),k=tx(s,uv+t*vec2(1,1)),l=tx(s,uv+t*vec2(-1,-1)),m=tx(s,uv+t*vec2(1,-1));
 return e*.125+(a+c+g+i)*.03125+(b+d+f+h)*.0625+(j+k+l+m)*.125;}

""";
    public const string BLOOM_PRE = """
void main(){vec3 c=d13(uSrc,uvOf(),uO.xy);vec4 cl=cloudAt(uvOf());c=mix(c,cl.rgb,cl.a);float b=max(c.r,max(c.g,c.b)),th=.9,kn=.12,s=clamp(b-th+kn,0.,2.*kn);s=s*s/(4.*kn+1e-4);
 o=vec4(c*max(s,b-th)/max(b,1e-4),1.);}
""";
    public const string BLOOM_DOWN = """
void main(){o=vec4(d13(uSrc,uvOf(),uO.xy),1.);}
""";
    public const string BLOOM_UP = """
void main(){vec2 uv=uvOf(),t=uO.xy;
 vec3 s=tx(uSrc,uv)*4.+(tx(uSrc,uv+vec2(t.x,0.))+tx(uSrc,uv-vec2(t.x,0.))+tx(uSrc,uv+vec2(0.,t.y))+tx(uSrc,uv-vec2(0.,t.y)))*2.
  +tx(uSrc,uv+t)+tx(uSrc,uv-t)+tx(uSrc,uv+vec2(t.x,-t.y))+tx(uSrc,uv+vec2(-t.x,t.y));
 o=vec4(s/16.,1.);}
""";
    public const string COMPOSITE = """
vec3 sc(vec2 uv){return min(texture(uSrc,uv).rgb,vec3(1.));}
float lu(vec3 c){return dot(c,vec3(.299,.587,.114));}
// FXAA (console-style): blur along the edge direction, keep the result only if it stays inside the local luma range
vec3 fxaa(vec2 uv,vec2 px){
 vec3 cM=sc(uv),cNW=sc(uv+px*vec2(-1,-1)),cNE=sc(uv+px*vec2(1,-1)),cSW=sc(uv+px*vec2(-1,1)),cSE=sc(uv+px*vec2(1,1));
 float lM=lu(cM),lNW=lu(cNW),lNE=lu(cNE),lSW=lu(cSW),lSE=lu(cSE),mn=min(lM,min(min(lNW,lNE),min(lSW,lSE))),mx=max(lM,max(max(lNW,lNE),max(lSW,lSE)));
 if(mx-mn<max(.0312,mx*.125))return cM;
 vec2 d=vec2(-((lNW+lNE)-(lSW+lSE)),(lNW+lSW)-(lNE+lSE));
 float rd=max((lNW+lNE+lSW+lSE)*.03125,1./128.),rc=1./(min(abs(d.x),abs(d.y))+rd);d=clamp(d*rc,-8.,8.)*px;
 vec3 a=.5*(sc(uv+d*(1./3.-.5))+sc(uv+d*(2./3.-.5))),b=a*.5+.25*(sc(uv-d*.5)+sc(uv+d*.5));float lb=lu(b);
 return(lb<mn||lb>mx)?a:b;}
void main(){vec2 uv=uvOf(),px=uO.xy;
 vec3 c=uFxaa>.5?fxaa(uv,px):sc(uv);
 // lower render resolution: the bilinear upscale softens the image, so sharpen a little (stronger the lower the scale)
 if(uRS<.99){vec3 nb=sc(uv+vec2(px.x,0.))+sc(uv-vec2(px.x,0.))+sc(uv+vec2(0.,px.y))+sc(uv-vec2(0.,px.y));c=clamp(c+(c-nb*.25)*(1.-uRS)*1.1,0.,1.);}
 vec4 cl=cloudAt(uv);c=mix(c,cl.rgb,cl.a);
 if(uBloomK>0.)c+=texture(uSrc2,uv).rgb*uBloomK;
 // god rays: march toward the sun through open sky (scene alpha 0 = sky), brightest close to the sun
 if(uRayK>0.){const int NS=36;vec2 d=(uSunUV-uv)/float(NS);vec2 p=uv+d*fract(52.9829189*fract(dot(gl_FragCoord.xy,vec2(.06711056,.00583715))));
  vec3 acc=vec3(0.);float il=1.;vec2 asp=vec2(uAsp,1.);
  for(int i=0;i<NS;i++){p+=d;vec2 pc=clamp(p,vec2(0.),vec2(1.));vec4 s=texture(uSrc,pc);float ca=uCloudOn>.5?texture(uSrc3,pc).a:0.;
   float w=exp(-length((p-uSunUV)*asp)*4.);acc+=max(min(s.rgb,vec3(3.))-.8,0.)*(1.-s.a)*(1.-ca)*il*w;il*=.965;}
  c+=acc*(uRayK/float(NS));}
 o=vec4(c,1.);}
""";
    public const string PHL_VS = """
layout(location=0)in vec3 aP;uniform mat4 uVP;void main(){gl_Position=uVP*vec4(aP,1.);}
""";
    public const string PHL_VS_PREFIX = "";
    public const string PHL_FS = """
uniform vec4 uK;out vec4 o;void main(){o=uK;}
""";
    public const string PHL_FS_PREFIX = "";
    public const bool PHL_COMMON = false;
}
