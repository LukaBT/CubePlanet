layout(location=0)in vec2 aP;layout(location=1)in vec2 aUv;layout(location=2)in vec4 aC;layout(location=3)in vec2 aM;
out vec2 vUv;out vec4 vC;flat out vec2 vM;
void main(){vec2 p=aP/uScr*2.-1.;gl_Position=vec4(p.x,p.y,0.,1.);vUv=aUv;vC=aC;vM=aM;}