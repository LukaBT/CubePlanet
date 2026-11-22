uniform vec3 uF,uRt,uUp;uniform float uTan,uAsp;in vec2 vQ;
vec3 viewRay(){return normalize(uF+uRt*(vQ.x*uTan*uAsp)+uUp*(vQ.y*uTan));}
