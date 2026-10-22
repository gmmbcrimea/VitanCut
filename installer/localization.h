#pragma once
#include <string>

namespace installer_l10n
{
struct Entry { const wchar_t* source; const wchar_t* values[4]; };
inline constexpr Entry Entries[] = {
    {L"Установка VitanCut", {L"VitanCut Setup", L"VitanCut installieren", L"Instalar VitanCut", L"安装 VitanCut"}},
    {L"VitanCut - проверка интерфейса", {L"VitanCut - UI preview", L"VitanCut - UI-Vorschau", L"VitanCut - Vista previa", L"VitanCut - 界面预览"}},
    {L"Установка программы", {L"Application setup", L"Programminstallation", L"Instalación del programa", L"程序安装"}},
    {L"Проверка интерфейса", {L"UI preview", L"UI-Vorschau", L"Vista previa", L"界面预览"}},
    {L"Язык", {L"Language", L"Sprache", L"Idioma", L"语言"}},
    {L"Папка установки", {L"Installation folder", L"Installationsordner", L"Carpeta de instalación", L"安装文件夹"}},
    {L"Программа будет установлена в выбранную папку.", {L"The app will be installed in the selected folder.", L"Installation im ausgewählten Ordner.", L"Se instalará en la carpeta seleccionada.", L"程序将安装到所选文件夹。"}},
    {L"Обзор", {L"Browse", L"Wählen", L"Examinar", L"浏览"}},
    {L"Ярлыки", {L"Shortcuts", L"Verknüpfungen", L"Accesos directos", L"快捷方式"}},
    {L"Ярлык на рабочем столе", {L"Desktop shortcut", L"Desktop-Verknüpfung", L"Acceso directo en el escritorio", L"桌面快捷方式"}},
    {L"Ярлык в меню Пуск", {L"Start menu shortcut", L"Startmenü-Verknüpfung", L"Acceso directo en Inicio", L"开始菜单快捷方式"}},
    {L"Закрепить на панели задач", {L"Pin to taskbar", L"An Taskleiste anheften", L"Anclar a la barra de tareas", L"固定到任务栏"}},
    {L"Установить", {L"Install", L"Installieren", L"Instalar", L"安装"}},
    {L"Проверить", {L"Test", L"Testen", L"Probar", L"测试"}},
    {L"Готово", {L"Done", L"Fertig", L"Listo", L"完成"}},
    {L"Повторить", {L"Retry", L"Erneut", L"Reintentar", L"重试"}},
    {L"Укажите папку установки.", {L"Choose an installation folder.", L"Wählen Sie einen Installationsordner.", L"Seleccione una carpeta de instalación.", L"请选择安装文件夹。"}},
    {L"Выберите папку для установки VitanCut", {L"Choose a folder for VitanCut", L"Ordner für VitanCut auswählen", L"Seleccione una carpeta para VitanCut", L"选择 VitanCut 安装文件夹"}},
    {L"Готовим установку...", {L"Preparing installation...", L"Installation wird vorbereitet...", L"Preparando la instalación...", L"正在准备安装..."}},
    {L"Проверяем интерфейс...", {L"Testing interface...", L"Oberfläche wird getestet...", L"Probando la interfaz...", L"正在测试界面..."}},
    {L"Получаем информацию о последней версии...", {L"Checking the latest version...", L"Neueste Version wird geprüft...", L"Buscando la última versión...", L"正在检查最新版本..."}},
    {L"Скачиваем программу...", {L"Downloading application...", L"Programm wird heruntergeladen...", L"Descargando el programa...", L"正在下载程序..."}},
    {L"Распаковываем файлы...", {L"Extracting files...", L"Dateien werden entpackt...", L"Extrayendo archivos...", L"正在解压文件..."}},
    {L"Устанавливаем VitanCut...", {L"Installing VitanCut...", L"VitanCut wird installiert...", L"Instalando VitanCut...", L"正在安装 VitanCut..."}},
    {L"VitanCut установлен. Программу можно открыть через созданный ярлык.", {L"VitanCut is installed. Open it using the shortcut.", L"VitanCut ist installiert. Starten Sie es über die Verknüpfung.", L"VitanCut está instalado. Ábralo desde el acceso directo.", L"VitanCut 已安装。请通过快捷方式打开。"}},
    {L"Не удалось подготовить Windows для создания ярлыков.", {L"Windows could not prepare shortcut creation.", L"Windows konnte keine Verknüpfungen vorbereiten.", L"Windows no pudo preparar los accesos directos.", L"Windows 无法准备创建快捷方式。"}},
    {L"Не удалось скачать программу. Проверьте подключение к интернету.", {L"Download failed. Check your internet connection.", L"Download fehlgeschlagen. Internetverbindung prüfen.", L"Error de descarga. Compruebe la conexión a Internet.", L"下载失败。请检查网络连接。"}},
    {L"В последнем релизе GitHub не найден архив VitanCut.", {L"No VitanCut archive found in the GitHub release.", L"Kein VitanCut-Archiv im GitHub-Release gefunden.", L"No se encontró el archivo VitanCut en GitHub.", L"未在 GitHub 发布中找到 VitanCut 压缩包。"}},
    {L"Не удалось распаковать архив программы.", {L"Could not extract the application archive.", L"Programmarchiv konnte nicht entpackt werden.", L"No se pudo extraer el archivo del programa.", L"无法解压程序文件。"}},
    {L"Программа установлена, но не удалось создать один из ярлыков.", {L"Installed, but a shortcut could not be created.", L"Installiert, aber eine Verknüpfung fehlt.", L"Instalado, pero no se pudo crear un acceso directo.", L"程序已安装，但无法创建某个快捷方式。"}},
    {L"Не удалось записать файлы в выбранную папку. Проверьте путь и права доступа.", {L"Cannot write to this folder. Check the path and permissions.", L"Schreiben nicht möglich. Pfad und Zugriffsrechte prüfen.", L"No se puede escribir. Compruebe la ruta y los permisos.", L"无法写入所选文件夹。请检查路径和权限。"}},
    {L"Проверка интерфейса: нет соединения. Установка не выполнялась.", {L"Preview: no connection. Nothing was installed.", L"Vorschau: keine Verbindung. Nichts wurde installiert.", L"Vista previa: sin conexión. No se instaló nada.", L"预览：无连接。未执行安装。"}},
    {L"Проверка интерфейса завершена. Файлы и ярлыки не изменялись.", {L"Preview complete. Files and shortcuts were not changed.", L"Vorschau beendet. Dateien und Verknüpfungen unverändert.", L"Vista previa completada. Sin cambios en archivos ni accesos.", L"预览完成。文件和快捷方式未更改。"}}
};

inline const wchar_t* Translate(const std::wstring& source, int language)
{
    if (language > 0 && language <= 4)
        for (const auto& entry : Entries)
            if (source == entry.source) return entry.values[language - 1];
    return source.c_str();
}
}
