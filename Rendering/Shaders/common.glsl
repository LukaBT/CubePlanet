
uniform float uWR,uWF,uWC;
uniform vec3 uS,uC,uWind;
uniform float uOvc,uStorm,uFlash,uSnow,uWet;
uniform float uR,uAT,uTime,uFog,uRc0,uRc1,uCov,uShOn,uCsOn;
uniform sampler3D uN;
float gAO=1.; // ambient occlusion of the current fragment (1 = open), set by the terrain shader
float gBl=0.; // block light (lamps) of the current fragment 0..1, set by the terrain shader
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
 vec3 lamp=vec3(1.,.78,.52)*1.6*gBl*gBl*gAO; // block light (lamps)
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
 // flowing water (gFlow): on top faces ripples and foam streaks drift along the flow, faster and whiter the stronger it is;
 // on side faces (a waterfall) white streaks run down
 if(gFlow.w>0.){
  float fs=gFlow.w,fl=1.-smoothstep(70.,260.,dist),ff=0.;vec3 fd=gFlow.xyz;
  if(topF>.5){
   float sp=.4+3.2*fs;vec3 pr=cross(up,fd);
   float ph=fract(T*.3),ph2=fract(T*.3+.5),wgt=abs(ph-.5)*2.;   // two phases of the moving pattern, blended so it never stretches
   vec3 a1=wp*.5-fd*(ph*sp*1.67),a2=wp*.5-fd*(ph2*sp*1.67);
   float h1=texture(uN,a1).r,h2=texture(uN,a2).r;
   vec2 g1=vec2(texture(uN,a1+t1*.03).r-h1,texture(uN,a1+t2*.03).r-h1),g2=vec2(texture(uN,a2+t1*.03).r-h2,texture(uN,a2+t2*.03).r-h2);
   vec2 gg=mix(g1,g2,wgt)*14.;nw=normalize(nw-(t1*gg.x+t2*gg.y)*(.05+.12*fs)*fl);
   float st=texture(uN,fd*(dot(wp,fd)*.12-T*sp*.12)+pr*dot(wp,pr)*.9+vec3(.37,.11,.73)).r;
   ff=smoothstep(.66-.18*fs,.86,st)*(.15+.75*fs);
  }else{
   vec3 pr=normalize(cross(up,ng));float st=texture(uN,vec3(dot(wp,pr)*1.4,dot(wp,up)*.16+T*(.5+1.5*fs),.53)).r;
   ff=smoothstep(.5-.25*fs,.8,st)*(.35+.6*fs);
  }
  ff*=fl;body=mix(body,lightIt(vec3(.92,.96,1.),nw,up,wp,1.,1.,dist),clamp(ff,0.,1.));foam=max(foam,ff);
 }
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
