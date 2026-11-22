layout(location=0)in vec3 aP;layout(location=1)in vec2 aA;layout(location=2)in vec3 aC;layout(location=3)in float aI;layout(location=4)in vec3 aN;
uniform mat4 uVP;uniform vec3 uO;out vec2 vA;out vec3 vC;out vec3 vP;out vec3 vN;
// distant terrain sits a little behind the voxel terrain: a proportional depth offset (reverse-Z: smaller = farther). A slope-scaled
// depth bias pushed steep skirts and horizon patches all the way to the far plane, where the sky was drawn over them (white seams).
void main(){vP=aP+uO;gl_Position=uVP*vec4(vP,1.);gl_Position.z*=.9985;vA=aA;vC=aC;vN=aN;}