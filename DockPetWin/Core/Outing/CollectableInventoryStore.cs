using System.IO;
using System.Text.Json;

namespace DockPetWin.Core.Outing;

public sealed class CollectableInventoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string inventoryPath;

    public CollectableInventoryStore()
    {
        var root = Path.Combine(
            AppContext.BaseDirectory,
            "UserData");
        inventoryPath = Path.Combine(root, "collectable-inventory.json");
    }

    public CollectableInventory Load()
    {
        try
        {
            return File.Exists(inventoryPath)
                ? JsonSerializer.Deserialize<CollectableInventory>(File.ReadAllText(inventoryPath), JsonOptions) ?? new CollectableInventory()
                : new CollectableInventory();
        }
        catch
        {
            return new CollectableInventory();
        }
    }

    public void Save(CollectableInventory inventory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(inventoryPath)!);
        File.WriteAllText(inventoryPath, JsonSerializer.Serialize(inventory, JsonOptions));
    }
}
