#version 450

layout(location = 10)
in vec3 fragColor;
layout(location = 11)
in vec2 fragTexCoord;

layout(location = 0)
out vec4 outColor0;

layout(binding = 0)
uniform sampler2D uTexture;

void main() {
	outColor0 = texture(uTexture, fragTexCoord) * vec4(fragColor, 1.0);
}
