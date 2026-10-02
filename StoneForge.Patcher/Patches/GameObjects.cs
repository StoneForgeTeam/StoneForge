using UndertaleModLib.Models;

namespace StoneForge.Patcher;

/// <summary>Adding objects to the game data (UndertaleModLib).</summary>
internal static class GameObjects
{
    /// <summary>A new object: its parent and sprite (by name), not persistent, visible.</summary>
    public static UndertaleGameObject Add(GameDataEditor editor, string name, string parent, string sprite)
    {
        var obj = editor.AddObject(name);
        obj.ParentId = editor.GetObject(parent);
        obj.Sprite = editor.GetSprite(sprite);
        obj.Persistent = false;
        obj.Visible = true;
        return obj;
    }
}
