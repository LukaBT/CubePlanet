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