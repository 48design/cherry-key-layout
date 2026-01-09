namespace CherryKeyLayout.Gui.Services
{
    public sealed class DeviceConfig
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = "Cherry MX Board";
        public string? ImagePath { get; set; }
        public string? LayoutPath { get; set; }
    }
}
