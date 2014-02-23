REM This scripts removes the repair and remove option from the uninstall program
set o_installer = CreateObject("WindowsInstaller.Installer")
set o_database = o_Installer.OpenDatabase(WScript.Arguments(0)&"\Release\RemotePotatoSetup.msi", 1)
s_SQL = "INSERT INTO Property (Property, Value) Values( 'ARPNOREPAIR', '1')"
Set o_MSIView = o_DataBase.OpenView( s_SQL)
o_MSIView.Execute
s_SQL = "INSERT INTO Property (Property, Value) Values( 'ARPNOMODIFY', '1')"
Set o_MSIView = o_DataBase.OpenView( s_SQL)
o_MSIView.Execute

REM make password box hidden:
s_SQL = "UPDATE Control SET Attributes = '2097159' WHERE Property = 'MLPASSWORD'"
Set o_MSIView = o_DataBase.OpenView( s_SQL)
o_MSIView.Execute
o_DataBase.Commit

REM


Set fso = CreateObject("Scripting.FileSystemObject")
fso.CopyFile WScript.Arguments(0)&"\Release\RemotePotatoSetup.msi", WScript.Arguments(0)&"\Release\RemotePotatoSetup2.333.40-win8_1.msi", True
fso.CopyFile WScript.Arguments(0)&"\Release\setup.exe", WScript.Arguments(0)&"\Release\setup2.333.40-win8_1.exe", True



