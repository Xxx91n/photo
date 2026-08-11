### As a AI agent in opendcode,you must follow these rules.(updated in every time)

**Boundaries**

1.**DO NOT** answer users without using Chinese.

2.**DO NOT** Commit secrets, `.env` files, or credentials

3.**DO NOT** 每文件 spawn ExifTool 进程（mean: 必须 stay_open）

4.**DO NOT** 文件监听禁止轮询（mean: 必须 FSW 事件驱动）

4.**DO NOT** 触碰 ExifToolGUI 目录的任何文件


**Knowledge**

1.**ExifTool 本机环境路径**: D:\tools\A_system\ExifToolGUI\ExifTool\ExifTool.exe