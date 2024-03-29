#version 450

layout(location = 0)
in vec3 inPosition;
layout(location = 1)
in vec3 inColor;
layout(location = 2)
in vec2 inTexCoord;

layout(location = 10)
out vec3 fragColor;
layout(location = 11)
out vec2 fragTexCoord;

void main() {
	gl_Position = vec4(inPosition.x, -inPosition.y, inPosition.z, 1.0);
	fragColor = inColor;
	fragTexCoord = inTexCoord;
}
