in float vI;in vec3 vQ;uniform float uNear;out vec4 o;
void main(){if(length(vQ)<uNear)discard;vec3 wp=vQ+uC,up=normalize(wp);float dist=length(vQ),al;vec3 V=vQ/dist;
 vec3 col=waterShade(wp,V,up,up,pow(vec3(.17,.36,.78),vec3(2.2)),dist,.85,al);
 if(vI>.5)col=glassIce(wp,V,up,up,vec4(.62,.78,.95,1.),6.,dist,1.,al);
 col=mix(col,skyFog(V,up),1.-exp(-dist*uFog));o=vec4(tmap(col),al);}