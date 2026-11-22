#version 450
layout(std140,set=0,binding=0) uniform Frame{
 mat4 uVP; mat4 uLV;
 vec3 uS; float uR; vec3 uC; float uAT; vec3 uWind; float uTime; vec3 uWdir; float uFog;
 vec3 uF; float uTan; vec3 uRt; float uAsp; vec3 uUp; float uShT; vec2 uScr; float uRc0; float uRc1;
 float uWA,uWL,uWK,uWD,uWTm,uWR0,uSea,uWR,uWF,uWC,uOvc,uStorm,uFlash,uSnow,uWet,uCov,uShOn,uCsOn,uWnd,uD,uNear,uRW,uCN,uCF;
 mat4 uPVP; mat4 uLV1; mat4 uLV2;
 vec3 uPC; float uSsr; vec2 uSunUV; float uRayK; float uBloomK;
 float uFxaa; float uShN; float uRS; float uPad1;
 vec3 uWindW; float uCloudQ; vec3 uWindD; float uCloudOn;
};
layout(push_constant) uniform PC{ vec3 uO; float uWt; vec4 uK; };
layout(set=0,binding=1) uniform sampler2DArray uT;
layout(set=0,binding=2) uniform sampler2DArrayShadow uSh;
layout(set=0,binding=3) uniform sampler3D uN;
layout(set=0,binding=4) uniform sampler2D uFont;
layout(set=0,binding=5) uniform sampler2D uHist;
layout(set=0,binding=6) uniform sampler2D uHistD;
layout(set=1,binding=0) uniform sampler2D uSrc;
layout(set=1,binding=1) uniform sampler2D uSrc2;
layout(set=1,binding=2) uniform sampler2D uSrc3;
layout(set=1,binding=3) uniform sampler2D uSrc4;
layout(set=1,binding=4) uniform sampler2D uSrc5;
layout(set=0,binding=7) uniform sampler3D uCloudN;
