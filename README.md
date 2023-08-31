# Simple-Recurrent-Denoiser
This is a relatively simple, and thus fast, recurrently blurring denoiser I made with some help and ideas from others</br>
It was designed from the ground up to be performant while still giving ok quality for noisy inputs such as pathtracing</br>

## Statistics:
All numbers for 1920x1080 on a (struggling)laptop 3080 with a blur radius of 30 and with 8 poisson samples</br>
<ul>
	<li>Total run time - about 3ms</li>
	<li>Total memory usage - about 128 megabytes</li>
</ul>

## Initializtion Inputs:
<ul>
	<li>ScreenWidth - width of the textures you plan to feed the denoiser</li>	
	<li>ScreenHeight - height of the textures you plan to feed the denoiser</li>
	<li>Blur Radius - radius of blur</li>
	<li>Camera - Main camera you will be rendering to(used here to get the transform of the camera automatically)</li>
	<li>Compute Shader - feed the included compute shader to this param</li>
</ul>

## Inputs:
<ul>
	<li>RadianceTex - float4 Render Texture that includes the combined demodulated radiance of specular and diffuse, and direct and indirect(add them all together)</li>
	<li>Albedo - Half4 Render Texture with the XYZ representing the albedo color of the primary hit surface, and the w is used to decide if the denoiser should ignore the pixel(if w is set to 0, else the pixel is denoised)</li>
	<li>Depth - Single Component Half Render Texture that stores the linearized depth of the scene</li>
	<li>GeomNorm - float4 Render Texture representing the geometric normals of the scene</li>
	<li>SurfNorm - Same as GeomNorm but stores surface normals(normals modified by any normaltextures on objects)</li>
	<li>MetRough - float2 Render Texture with surface metallic in the X component and surface roughness in the Y component</li>
</ul>

## Note:
The input radiances before adding them together must not include the primary hit BRDF/Albedo, otherwise we would be blurring things like textures</br>

## Tips:
To handle speculars you gotta do something special thats weird, but it lets me store all lighting in a single buffer</br>
If you have first bounce next event estimation, you start by taking your first bounce NEE irradiance and multiply it by 1 / the primary hit brdf value</br>
Next, add together the direct, indirect, and specular signals(and primary NEE if you have it)</br>
Next, multiply the result by the primary hit brdf value</br>
Finally, divide that result by the primary hit ALBEDO color to remove the textures from the signal so they can be re-applied after denoising</br>
You can do all of this in a preprocessing stage and just feed this result as the irradiance texture</br>
