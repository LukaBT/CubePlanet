
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
// gFlow: flowing water (xyz world direction, w strength 0..1), set by terrain.frag
float gCalm=0.;vec4 gFlow=vec4(0.);