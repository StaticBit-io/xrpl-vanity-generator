# Linux test image: the local .NET 10 SDK image (Ubuntu 24.04) plus pocl, an independent
# OpenCL implementation running on the CPU, and the ICD loader. CUDA reaches the container
# through `docker run --gpus all` (the NVIDIA container runtime injects libcuda.so.1).
FROM mcr.microsoft.com/dotnet/sdk:10.0

# DOTNET_ROLL_FORWARD: DartSassBuilder, which compiles XrplVanity.App.Core's SCSS during the
# build, ships a net8.0 tool, and this image carries only the .NET 10 runtime. Without the roll
# forward the tool aborts with "You must install or update .NET" and the build fails.
ENV DEBIAN_FRONTEND=noninteractive \
    NVIDIA_DRIVER_CAPABILITIES=compute,utility \
    POCL_CACHE_DIR=/cache/pocl \
    NUGET_PACKAGES=/cache/nuget \
    DOTNET_ROLL_FORWARD=LatestMajor \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

RUN apt-get update \
    && apt-get install -y --no-install-recommends ocl-icd-libopencl1 pocl-opencl-icd clinfo \
    && rm -rf /var/lib/apt/lists/*

COPY in-container.sh /usr/local/bin/xrpl-vanity-linux-tests
RUN chmod +x /usr/local/bin/xrpl-vanity-linux-tests

WORKDIR /work
ENTRYPOINT ["/usr/local/bin/xrpl-vanity-linux-tests"]
