using System.Text;
using System.IO;
using System.Xml.Linq;
using System.Linq;
public class OfficeConfiguration
{
    // Обязательный: Office LTSC Professional Plus 2024
    public bool InstallProPlus { get; set; } = true; // По умолчанию всегда выбран

    // Продукты
    public bool InstallVisio { get; set; } = false; // Визио
    public bool InstallProject { get; set; } = false; // Проджект

    // Приложения ProPlus
    public bool ExcludeAccess { get; set; } = false; // Исключить Access
    public bool ExcludeExcel { get; set; } = false; // Исключить Excel
    public bool ExcludeWord { get; set; } = false; // Исключить Word
    public bool ExcludePowerPoint { get; set; } = false; // Исключить PowerPoint
    public bool ExcludeOutlook { get; set; } = true; // Исключить Outlook (по дефолту в вашем XML)

    // Дополнительные исключения (из дефолтного XML)
    public bool ExcludeLync { get; set; } = true;
    public bool ExcludeOneDrive { get; set; } = true;
    public bool ExcludeOneNote { get; set; } = true;
    public bool ExcludePublisher { get; set; } = true;
}
public static class ConfigurationGenerator
{
    private const string ProPlus2024VolumeID = "ProPlus2024Volume";
    private const string VisioPro2024VolumeID = "VisioPro2024Volume";
    private const string ProjectPro2024VolumeID = "ProjectPro2024Volume";

    public static string GenerateXml(OfficeConfiguration config)
    {
        var root = new XElement("Configuration",
            new XAttribute("ID", Guid.NewGuid().ToString())
        );

        var addElement = new XElement("Add",
            new XAttribute("OfficeClientEdition", "64"),
            new XAttribute("Channel", "PerpetualVL2024")
        );

        // --- Office LTSC Professional Plus 2024 ---
        if (config.InstallProPlus)
        {
            var proPlusProduct = new XElement("Product",
                new XAttribute("ID", ProPlus2024VolumeID),
                new XAttribute("PIDKEY", "XJ2XN-FW8RK-P4HMP-DKDBV-GCVGB"), // Ваш ключ
                new XElement("Language", new XAttribute("ID", "ru-ru"))
            );

            // Exclude Apps logic
            var excludeList = new List<Tuple<string, bool>>()
            {
                // Приложения ProPlus, которые можно исключить
                Tuple.Create("Access", config.ExcludeAccess),
                Tuple.Create("Excel", config.ExcludeExcel),
                Tuple.Create("Word", config.ExcludeWord),
                Tuple.Create("PowerPoint", config.ExcludePowerPoint),
                Tuple.Create("Outlook", config.ExcludeOutlook),

                // Стандартные исключения из вашего дефолтного XML
                Tuple.Create("Lync", config.ExcludeLync),
                Tuple.Create("OneDrive", config.ExcludeOneDrive),
                Tuple.Create("OneNote", config.ExcludeOneNote),
                Tuple.Create("Publisher", config.ExcludePublisher)
            };

            foreach (var item in excludeList.Where(i => i.Item2))
            {
                proPlusProduct.Add(new XElement("ExcludeApp", new XAttribute("ID", item.Item1)));
            }

            addElement.Add(proPlusProduct);
        }

        // --- Visio Pro LTSC 2024 ---
        if (config.InstallVisio)
        {
            var visioProduct = new XElement("Product",
                new XAttribute("ID", VisioPro2024VolumeID),
                new XAttribute("PIDKEY", "B7TN8-FJ8V3-7QYCP-HQPMV-YY89G"), // Ваш ключ
                new XElement("Language", new XAttribute("ID", "ru-ru"))
            );
            // Приложения Visio также могут иметь исключения, но для простоты используем те же, что и для ProPlus
            var excludeVisioList = new List<Tuple<string, bool>>()
            {
                Tuple.Create("Lync", config.ExcludeLync),
                Tuple.Create("OneDrive", config.ExcludeOneDrive),
                Tuple.Create("OneNote", config.ExcludeOneNote),
                Tuple.Create("Outlook", config.ExcludeOutlook),
                Tuple.Create("Publisher", config.ExcludePublisher)
            };
            foreach (var item in excludeVisioList.Where(i => i.Item2))
            {
                visioProduct.Add(new XElement("ExcludeApp", new XAttribute("ID", item.Item1)));
            }

            addElement.Add(visioProduct);
        }

        // --- Project Pro LTSC 2024 (Добавлено для полноты) ---
        if (config.InstallProject)
        {
            var projectProduct = new XElement("Product",
                new XAttribute("ID", ProjectPro2024VolumeID),
                new XAttribute("PIDKEY", "N24V3-49F8P-K9C8M-M3C4X-469XG"), // Пример ключа
                new XElement("Language", new XAttribute("ID", "ru-ru"))
            );
            addElement.Add(projectProduct);
        }

        root.Add(addElement);

        // --- Дополнительные свойства и настройки из вашего дефолтного XML ---
        root.Add(
            new XElement("Property", new XAttribute("Name", "SharedComputerLicensing"), new XAttribute("Value", "0")),
            new XElement("Property", new XAttribute("Name", "FORCEAPPSHUTDOWN"), new XAttribute("Value", "FALSE")),
            new XElement("Property", new XAttribute("Name", "DeviceBasedLicensing"), new XAttribute("Value", "0")),
            new XElement("Property", new XAttribute("Name", "SCLCacheOverride"), new XAttribute("Value", "0")),
            new XElement("Property", new XAttribute("Name", "AUTOACTIVATE"), new XAttribute("Value", "0")),
            new XElement("Updates", new XAttribute("Enabled", "TRUE")),
            new XElement("RemoveMSI"),
            new XElement("AppSettings",
                new XElement("User", new XAttribute("Key", "software\\microsoft\\office\\16.0\\excel\\options"), new XAttribute("Name", "defaultformat"), new XAttribute("Value", "51"), new XAttribute("Type", "REG_DWORD"), new XAttribute("App", "excel16"), new XAttribute("Id", "L_SaveExcelfilesas")),
                new XElement("User", new XAttribute("Key", "software\\microsoft\\office\\16.0\\powerpoint\\options"), new XAttribute("Name", "defaultformat"), new XAttribute("Value", "27"), new XAttribute("Type", "REG_DWORD"), new XAttribute("App", "ppt16"), new XAttribute("Id", "L_SavePowerPointfilesas")),
                new XElement("User", new XAttribute("Key", "software\\microsoft\\office\\16.0\\word\\options"), new XAttribute("Name", "defaultformat"), new XAttribute("Value", ""), new XAttribute("Type", "REG_SZ"), new XAttribute("App", "word16"), new XAttribute("Id", "L_SaveWordfilesas"))
            )
        );

        // Используем XDocument для красивого форматирования XML
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
        return doc.ToString();
    }
}