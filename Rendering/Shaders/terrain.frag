in vec2 vUv;flat in float vL;in float vS,vO;in vec3 vN;in vec3 vP;in float vAO;flat in uint vF;flat in vec4 vFl;
out vec4 o;layout(location=1) out float oD;
void main(){gFlow=vFl;vec4 c=texture(uT,vec3(vUv,vL));if(uWt<.5&&c.a<.5)discard;
 vec3 alb=pow(c.rgb,vec3(2.2)),n=normalize(vN),wp=vP+uC,up=normalize(wp),col;float dist=length(vP),al=1.;vec3 V=vP/dist;if(uWt<.5)alb=wxAlb(alb,n,up,vS);
 gCalm=1.-vO;if(uWt>1.5)col=glassIce(wp,V,n,up,c,(vF&32u)!=0u?0.:20.,dist,vS,al);else if(uWt>.5)col=waterShade(wp,V,n,up,alb,dist,vS,al);else{float a=vAO/3.;gAO=mix(.38,1.,a*a*(3.-2.*a));col=lightIt(alb,n,up,wp,vS,min(shadowAt(vP,n),smoothstep(.55,.95,vS)),dist);}
 col=mix(col,skyFog(V,up),(1.-exp(-dist*uFog))*mix(.1,1.,(uWt>.5&&uWt<1.5)?1.:vS));o=vec4(tmap(col),al);oD=dist;
 if(uWt<.5&&(vF&8u)!=0u)o.rgb+=c.rgb*c.rgb*1.3*exp(-dist*uFog);} // emissive blocks (lava, magma, sea lanterns) glow, picked up by bloom