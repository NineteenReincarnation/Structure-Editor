using System.Xml.Linq;

namespace StructureEditor;

public partial class MainForm
{
    private readonly TextBox saveFolderBox=new(){ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical};
    private void RefreshSaveLocation()
    {
        saveFolderBox.Text=!saveLocationChanged&&currentFile?.EndsWith(".json",StringComparison.OrdinalIgnoreCase)==true?Path.GetDirectoryName(currentFile):OutputFolder;
        RefreshExportLocation();
    }
    private void SetSaveDirectory()
    {
        using FolderBrowserDialog dialog=new(){Description="选择工程保存目录",SelectedPath=OutputFolder,UseDescriptionForTitle=true};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        Program.Paths.SaveDirectory=dialog.SelectedPath;Program.SavePaths();saveLocationChanged=true;RefreshSaveLocation();status.Text="工程保存目录："+dialog.SelectedPath;
    }
    private bool SaveDocument(bool choose)
    {
        if(namingSwitchDelay.Enabled)ResolveNamingSelection();
        CommitInspectorEdit();CommitMetadata();_ = document.ToXml();
        string previousId=currentFile?.EndsWith(".json",StringComparison.OrdinalIgnoreCase)==true&&File.Exists(currentFile)?StructureDocument.Load(currentFile).DefName:null;
        string path=StructureStorage.ResolveTarget(currentFile,previousId,document.DefName,OutputFolder,saveLocationChanged);
        if(choose||currentFile==null)
        {
            using SaveFileDialog dialog=new(){Title="保存结构工程",Filter="结构工程|*.structure.json",InitialDirectory=Path.GetDirectoryName(path),FileName=Path.GetFileName(path),DefaultExt="structure.json",AddExtension=true};
            if(dialog.ShowDialog(this)!=DialogResult.OK)return false;path=dialog.FileName;
        }
        StructureStorage.CheckDuplicate(path,document.DefName,Path.GetDirectoryName(path)!);
        CheckWorkspaceSaveTarget(path);
        var result=StructureStorage.SaveProject(document,catalog,path);currentFile=path;saveLocationChanged=false;dirty=false;
        activeDocument.LastSavedId=document.DefName;if(result.ExportError==null){activeDocument.LastXmlPath=result.XmlPath;MP_RecordExportTarget(result.XmlPath);}
        Program.Paths.SaveDirectory=Path.GetDirectoryName(path);RememberDocument(path);PersistWorkspace();RefreshSaveLocation();UpdateTitle();
        if (MP_libraryLoaded) MP_ReloadLibrary();
        exportResult.Text=result.ExportError!=null?"工程已保存；XML 未更新：\n"+result.ExportError:result.XmlPath!=null?"XML 已更新：\n"+result.XmlPath:"工程已保存（自动导出已关闭）。";
        exportResult.ForeColor=result.ExportError==null?muted:Color.Salmon;
        exportResult.Visible = true;
        status.Text=result.ExportError!=null?"工程已保存；XML 导出失败："+result.ExportError:result.XmlPath!=null?"工程和 XML 已保存："+result.XmlPath:"已保存工程："+path;
        if(result.ExportError!=null) MessageBox.Show(this,"JSON 工程已保存。XML 未更新：\n\n"+result.ExportError+"\n\n请检查工程页的导出问题或调整 XML 目录后重试。","自动导出失败",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        return true;
    }
    private void ExportLayout()
    {
        CommitInspectorEdit();CommitMetadata();
        var review=MP_ExportReview.Inspect(document,catalog,"Framework");
        if(!review.CanExport)
        {
            MessageBox.Show(this,string.Join("\n",review.Issues.Where(i=>i.IsError).Take(12).Select(i=>i.Message)),
                "导出检查",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            return;
        }
        using SaveFileDialog dialog=new()
        {
            Title="导出 Structure Framework XML（"+review.Dependencies.Count+" 个依赖）",
            Filter="布局 XML|*.xml",
            InitialDirectory=Path.GetDirectoryName(StructureStorage.XmlTarget(document,currentFile,OutputFolder)),
            FileName=document.DefName+".xml",DefaultExt="xml",AddExtension=true
        };
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        CheckWorkspaceExportTarget(dialog.FileName);
        StructureStorage.ExportXml(document,catalog,dialog.FileName,"Framework");
        MP_RecordExportTarget(dialog.FileName);
        status.Text="已导出 Structure Framework 布局："+dialog.FileName;
    }
    private void ExportBundle()
    {
        CommitInspectorEdit();CommitMetadata();
        var review=MP_ExportReview.Inspect(document,catalog,"Framework");
        if(!review.CanExport)throw new InvalidOperationException(string.Join("\n",review.Issues.Where(i=>i.IsError).Take(12).Select(i=>i.Message)));
        using FolderBrowserDialog folder=new(){Description="选择内容包的父目录（不会复制模组素材或 DLL）",SelectedPath=OutputFolder};if(folder.ShowDialog(this)!=DialogResult.OK)return;
        string result=ExportDependencies.Bundle(document,catalog,folder.SelectedPath,"Framework");status.Text="已导出独立结构包："+result;
    }
    private StructureDocument LoadChosenDefinition(string file)
    {
        if(file.EndsWith(".json",StringComparison.OrdinalIgnoreCase))return StructureDocument.Load(file,catalog);
        var root=XDocument.Load(file).Root;
        var choices=(root.Name.LocalName=="Defs"?root.Elements():new[]{root}).Where(e=>e.Name.LocalName is "PrefabDef" or "StructureFramework.StructureDef" or "MechProtocol.MP_StructureDef" or "StructureEditor.StructureDef").Where(e=>!string.Equals((string)e.Attribute("Abstract"),"true",StringComparison.OrdinalIgnoreCase)).ToList();
        if(choices.Count<=1)return StructureDocument.Load(file,catalog);
        using Form picker=new(){Text="选择要打开的结构",Size=new(540,430),StartPosition=FormStartPosition.CenterParent,Font=Font,BackColor=surface,ForeColor=text};
        ListBox list=new(){Dock=DockStyle.Fill,BackColor=surface,ForeColor=text};foreach(var choice in choices)list.Items.Add((string)choice.Element("defName"));list.SelectedIndex=0;
        Button open=MakeButton("打开",0,0,90);open.Dock=DockStyle.Bottom;open.Click+=(_,_)=>picker.DialogResult=DialogResult.OK;list.DoubleClick+=(_,_)=>picker.DialogResult=DialogResult.OK;picker.Controls.Add(list);picker.Controls.Add(open);
        return picker.ShowDialog(this)==DialogResult.OK?StructureDocument.Load(file,catalog,list.SelectedItem?.ToString()):null;
    }
    private void RememberDocument(string path)
    {
        Program.Paths.LastDocument=path;Program.Paths.LastDefinition=document.DefName;Program.Paths.RecentDocuments.RemoveAll(p=>p.Equals(path,StringComparison.OrdinalIgnoreCase));Program.Paths.RecentDocuments.Insert(0,path);
        Program.Paths.RecentDocuments=Program.Paths.RecentDocuments.Take(12).ToList();Program.SavePaths();
    }
}
