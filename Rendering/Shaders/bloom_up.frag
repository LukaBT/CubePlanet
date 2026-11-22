void main(){vec2 uv=uvOf(),t=uO.xy;
 vec3 s=tx(uSrc,uv)*4.+(tx(uSrc,uv+vec2(t.x,0.))+tx(uSrc,uv-vec2(t.x,0.))+tx(uSrc,uv+vec2(0.,t.y))+tx(uSrc,uv-vec2(0.,t.y)))*2.
  +tx(uSrc,uv+t)+tx(uSrc,uv-t)+tx(uSrc,uv+vec2(t.x,-t.y))+tx(uSrc,uv+vec2(-t.x,t.y));
 o=vec4(s/16.,1.);}