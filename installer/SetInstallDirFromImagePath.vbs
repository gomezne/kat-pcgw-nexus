Function GetDirectoryName(FilePath)
  GetDirectoryName = CreateObject("Scripting.FileSystemObject").GetParentFolderName(FilePath)
End Function
Session.Property("INSTALLDIR") = GetDirectoryName(Replace(Session.Property("SERVICE_INSTALLED"), """", ""))