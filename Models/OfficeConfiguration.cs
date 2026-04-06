using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Helinstaller.Models
{
    public class OfficeConfiguration
    {
        // Основной пакет
        public bool InstallProPlus { get; set; } = true;

        // Приложения внутри ProPlus
        public bool IncludeWord { get; set; } = true;
        public bool IncludeExcel { get; set; } = true;
        public bool IncludePowerPoint { get; set; } = true;
        public bool IncludeOutlook { get; set; } = true;
        public bool IncludeAccess { get; set; } = true;

        // Отдельные продукты
        public bool InstallVisio { get; set; } = false;
        public bool InstallProject { get; set; } = false;

        public string GenerateXml()
        {
            // Определяем язык системы (например, ru-ru)
            string lang = CultureInfo.CurrentUICulture.Name.ToLower();

            // Настройка для Office LTSC 2024 Pro Plus
            XElement addElement = new XElement("Add",
                new XAttribute("OfficeClientEdition", "64"),
                new XAttribute("Channel", "PerpetualVL2024")
            );

            // Основной продукт
            XElement proPlus = new XElement("Product", new XAttribute("ID", "ProPlus2024Volume"));
            proPlus.Add(new XElement("Language", new XAttribute("ID", lang)));

            // Логика исключений: если галочка НЕ стоит, добавляем в ExcludeApp
            if (!IncludeWord) proPlus.Add(new XElement("ExcludeApp", new XAttribute("ID", "Word")));
            if (!IncludeExcel) proPlus.Add(new XElement("ExcludeApp", new XAttribute("ID", "Excel")));
            if (!IncludePowerPoint) proPlus.Add(new XElement("ExcludeApp", new XAttribute("ID", "PowerPoint")));
            if (!IncludeOutlook) proPlus.Add(new XElement("ExcludeApp", new XAttribute("ID", "Outlook")));
            if (!IncludeAccess) proPlus.Add(new XElement("ExcludeApp", new XAttribute("ID", "Access")));

            // LTSC 2024 по умолчанию не включает Teams и Skype, но на всякий случай исключим старое:
            proPlus.Add(new XElement("ExcludeApp", new XAttribute("ID", "Lync")));
            proPlus.Add(new XElement("ExcludeApp", new XAttribute("ID", "Groove")));

            addElement.Add(proPlus);

            // Доп продукты
            if (InstallVisio)
            {
                XElement visio = new XElement("Product", new XAttribute("ID", "VisioPro2024Volume"));
                visio.Add(new XElement("Language", new XAttribute("ID", lang)));
                addElement.Add(visio);
            }

            if (InstallProject)
            {
                XElement project = new XElement("Product", new XAttribute("ID", "ProjectPro2024Volume"));
                project.Add(new XElement("Language", new XAttribute("ID", lang)));
                addElement.Add(project);
            }

            XDocument doc = new XDocument(
                new XElement("Configuration",
                    addElement,
                    new XElement("RemoveMSI"),
                    new XElement("Display", new XAttribute("Level", "Full"), new XAttribute("AcceptEULA", "TRUE")),
                    new XElement("Property", new XAttribute("Name", "AUTOACTIVATE"), new XAttribute("Value", "1"))
                )
            );

            return doc.ToString();
        }
    }
}