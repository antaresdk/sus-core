using UnityEditor;

namespace Sharq.Core.Editor
{
    /// <summary>
    /// Registers <c>Assets → Create → SUS → ...</c> menu items that create
    /// .sharq files from templates in <c>Editor/Templates/</c>.
    /// </summary>
    internal static class SusScriptTemplateMenu
    {
        private const string TemplateDir = "Packages/com.sharq-it.sus.core/Editor/Templates/";

        [MenuItem("Assets/Create/SUS/Sharq Component", false, 80)]
        private static void CreateSharqComponent()
        {
            ProjectWindowUtil.CreateScriptAssetFromTemplateFile(
                TemplateDir + "80-SUS__Sharq Component-NewSharqComponent.sharq.txt",
                "NewSharqComponent.sharq");
        }

        [MenuItem("Assets/Create/SUS/Sharq Screen", false, 81)]
        private static void CreateSharqScreen()
        {
            ProjectWindowUtil.CreateScriptAssetFromTemplateFile(
                TemplateDir + "81-SUS__Sharq Screen-NewSharqScreen.sharq.txt",
                "NewSharqScreen.sharq");
        }

        [MenuItem("Assets/Create/SUS/Sharq Modal", false, 82)]
        private static void CreateSharqModal()
        {
            ProjectWindowUtil.CreateScriptAssetFromTemplateFile(
                TemplateDir + "82-SUS__Sharq Modal-NewSharqModal.sharq.txt",
                "NewSharqModal.sharq");
        }
    }
}
