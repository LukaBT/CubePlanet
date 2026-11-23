in vec2 vUv;in vec4 vC;flat in vec2 vM;out vec4 o;
void main(){vec4 c=vC;if(vM.x>3.5){c*=texture(uItems,vec3(vUv,vM.y));}else if(vM.x>2.5){float sz=float(textureSize(uItems,0).x),lod=log2(max(max(length(dFdx(vUv)),length(dFdy(vUv)))*sz,1.));c*=textureLod(uItems,vec3(vUv,vM.y),min(floor(lod),2.));}else if(vM.x>1.5){vec4 t=texture(uT,vec3(vUv,vM.y));c*=t;}else if(vM.x>.5){c.a*=texture(uFont,vUv).r;}o=c;}
// mode 4: 3D block icons, trilinear. mode 3: flat pictures (scaled up by whole pixels to 128 px): bilinear from the largest mip level that is
// still at least as big as the icon on screen (at most level 2 = 32 px, the item atlas size), so pixels stay square and sharp ("sharp bilinear")
