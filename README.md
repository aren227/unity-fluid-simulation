# unity-fluid-simulation
![image_004_0485](https://user-images.githubusercontent.com/14082448/197345770-a1151a75-f811-428e-898b-a0d6d5938acb.png)
Real-time fluid simulation in unity.

## Features
- Based on Smoothed-particle hydrodynamics method.
- Runs entirely on GPU.
- Handles up to one million particles.
- Uses counting sort to speed-up neighbor searching.
- Implementing surface extraction techniques proposed by Yu and Turk.
- Performs normal smoothing to make the surface more realistic while avoiding expensive isosurface extraction algorithms.
- Basic water shading

## Demo Video
https://youtu.be/s-cDYtNfsl4

One million particles simulated in real-time. ~40 fps with GTX 1070 Ti.

## References
- M. Müller, D. Charypar and M. Gross. Particlebased fluid simulation for interactive applications. 2003.
- J. Yu and G. Turk. Reconstructing Surfaces of Particle-Based Fluids Using Anisotropic Kernels. 2010.
- Rama C. Hoetzlein. Fast Fixed-Radius Nearest Neighbor Search on the GPU. 2014.
