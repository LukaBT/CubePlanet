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