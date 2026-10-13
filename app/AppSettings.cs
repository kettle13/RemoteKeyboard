using System.Text.Json;

namespace RemoteInput;

/// <summary>%APPDATA%\RemoteInput\settings.json に保存する設定。</summary>
internal sealed class AppSettings
{
    public const int MinReleaseKey = 8;
    public const int MaxReleaseKey = 12;
    public const int MinPointerSpeed = 50;
    public const int MaxPointerSpeed = 400;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RemoteInput", "settings.json");

    /// <summary>前回選んだボードのUSB Serial Number。</summary>
    public string? BoardSerial { get; set; }

    /// <summary>解除キーのファンクションキー番号(F8～F12)。</summary>
    public int ReleaseKey { get; set; } = 12;

    /// <summary>ホイール1ノッチあたりに送るスクロール量。</summary>
    public int ScrollScale { get; set; } = 1;

    /// <summary>ホイールの向きを逆にする(縦横とも)。</summary>
    public bool InvertScroll { get; set; }

    /// <summary>マウス移動量の倍率(%)。</summary>
    public int PointerSpeed { get; set; } = 100;

    /// <summary>
    /// 接続先ごとの設定。キーは「ボードのUSB Serial Number#スロット番号」。
    /// </summary>
    public Dictionary<string, TargetProfile> Profiles { get; set; } = [];

    public TargetProfile GetProfile(string boardSerial, int slot)
    {
        string key = $"{boardSerial}#{slot}";
        if (!Profiles.TryGetValue(key, out TargetProfile? profile))
        {
            // スロット導入前はボード単位で保存していたので、スロット1へ引き継ぐ
            if (slot == 1 && Profiles.Remove(boardSerial, out TargetProfile? legacy))
            {
                profile = legacy;
            }
            else
            {
                profile = new TargetProfile();
            }
            Profiles[key] = profile;
        }
        return profile;
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
                settings.ReleaseKey = Math.Clamp(settings.ReleaseKey, MinReleaseKey, MaxReleaseKey);
                settings.ScrollScale = Math.Clamp(settings.ScrollScale, 1, 20);
                settings.PointerSpeed = Math.Clamp(settings.PointerSpeed, MinPointerSpeed, MaxPointerSpeed);
                return settings;
            }
        }
        catch (Exception)
        {
            // 壊れた設定ファイルは既定値で置き換える
        }
        return new AppSettings();
    }

    /// <summary>保存できなくても動作は続けられるよう例外は投げず、失敗した場合はその理由を返す。</summary>
    public string? Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
