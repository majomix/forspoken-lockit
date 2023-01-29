using ForspokenEarcLib;


var importer = new EarcImporter();
var fileSizeOffset = 1270396;
var fileOffsetOffset = 1270408;
var filePath = @"G:\SteamLibrary\steamapps\common\FORSPOKEN Demo\datas\c000_001.earc";
var textPath = @"D:\Preklady\Forspoken\Demo\text_us_46a51283.parambin";
importer.ImportTextFile(fileSizeOffset, fileOffsetOffset, textPath, filePath);