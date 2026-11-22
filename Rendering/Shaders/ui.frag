in vec2 vUv;in vec4 vC;flat in vec2 vM;out vec4 o;
void main(){vec4 c=vC;if(vM.x>1.5){vec4 t=texture(uT,vec3(vUv,vM.y));c*=t;}else if(vM.x>.5){c.a*=texture(uFont,vUv).r;}o=c;}