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
