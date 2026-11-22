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