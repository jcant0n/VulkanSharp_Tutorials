using Vulkan;

namespace VKUniformBuffers
{
    public struct SwapChainSupportDetails
    {
        public SurfaceCapabilitiesKhr capabilities;
        public SurfaceFormatKhr[] formats;
        public PresentModeKhr[] presentModes;

        public bool IsComplete => formats.Length > 0 && presentModes.Length > 0;

        public SwapChainSupportDetails(PhysicalDevice device, SurfaceKhr surface)
        {
            capabilities = device.GetSurfaceCapabilitiesKHR(surface);
            formats = device.GetSurfaceFormatsKHR(surface);
            presentModes = device.GetSurfacePresentModesKHR(surface);
        }
    }
}
