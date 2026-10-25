
using System;

namespace NFOForge.App.Models
{
    public sealed class NfoTemplate
    {
        public string Id { get; set; } =
            Guid.NewGuid().ToString("N");

        public string Name { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        public bool IsBuiltIn { get; set; }
    }
}
