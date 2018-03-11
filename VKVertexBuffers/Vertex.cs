using System.Numerics;
using Vulkan;

namespace VKVertexBuffers
{
    public struct Vertex
    {
        public Vector2 Position { get; private set; }
        public Vector3 Color { get; private set; }

        public Vertex(Vector2 position, Vector3 color)
        {
            this.Position = position;
            this.Color = color;
        }

        public static VertexInputBindingDescription[] GetBindingDescription()
        {
            var bindingDescription = new VertexInputBindingDescription[]
            {
                new VertexInputBindingDescription()
                {
                    Binding = 0,
                    Stride = 5 * sizeof(float),
                    InputRate = VertexInputRate.Vertex,
                }
            };

            return bindingDescription;
        }

        public static VertexInputAttributeDescription[] GetAttributeDescription()
        {
            var attributeDescriptions = new VertexInputAttributeDescription[]
            {
                new VertexInputAttributeDescription()
                {
                    Binding = 0,
                    Location = 0,
                    Format = Format.R32G32Sfloat,
                    Offset = 0,
                },
                new VertexInputAttributeDescription()
                {
                    Binding = 0,
                    Location = 1,
                    Format = Format.R32G32B32Sfloat,
                    Offset = 2 * sizeof(float),
                }
            };

            return attributeDescriptions;
        }
    }
}
