out vec4 o;
void main(){vec3 d=viewRay();float r=length(uC),alt=r-uR,w=smoothstep(.3*uAT,.9*uAT,alt);if(w<=0.)discard;
 float tc=max(-dot(uC,d),0.);vec3 P=uC+d*tc;float b=length(P),x=(b-uR)/uAT;if(x>1.)discard;
 float g=x>=0.?pow(1.-x,6.):exp(x*20.);float nd=dot(P/b,uS),lit=smoothstep(-.35,.4,nd),tw=1.-smoothstep(0.,.5,abs(nd));
 o=vec4(mix(vec3(.22,.48,1.),vec3(1.,.45,.18),tw*.8)*lit*g*w*1.1,1.);}