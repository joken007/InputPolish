@echo off
rem 仅用于自动化测试：允许钩子接受 SendInput 注入的空格键
set INPUTPOLISH_ACCEPT_INJECTED=1
start "" "%~dp0..\dist\InputPolish.exe"
exit
