# FGOAC scooby {{VERSION}} - FGO Arcade เป็นภาษาไทย

แพตช์ภาษาไทยสำหรับ FGO Arcade local platform แพตช์นี้แปลตัวเกมเองเป็นภาษาไทย ทั้งเมนู บทเรียนเริ่มต้น
ข้อความเนื้อเรื่อง หน้าจอต่อสู้ ร้านค้า และหน้าช่วยเหลือ แล้วแทนที่หน้าจอหลักภาษาจีนด้วยหน้าจอภาษาไทย
ชื่อ **FGOAC scooby** ซึ่งเริ่มเซิร์ฟเวอร์ให้ จัดการบัญชีของคุณ และประกอบเด็คการ์ดให้

This is not a game download. It does nothing on its own: you need an existing FGO Arcade local
platform install (Cloud23333's package at V1.01 or V1.02, with or without his latest update; V1.02 is
the one to be on), and the patch is applied on top of it.

## การติดตั้ง

1. แตกไฟล์แพ็กเกจนี้ลงในโฟลเดอร์ FGO Arcade ของคุณ ซึ่งเป็นโฟลเดอร์ที่มี `App` และ `Server` อยู่
2. เรียกใช้ **FGOAC scooby**
3. คลิก Yes ในหน้าต่างขออนุญาตของ Windows

การเริ่มครั้งแรกจะจัดการส่วนที่เหลือให้เอง คือติดตั้งไฟล์ภาษาไทย ตรวจสอบว่าเกมเขียนลงโฟลเดอร์ของตัวเองได้
อนุญาตให้เกมและเซิร์ฟเวอร์ในเครื่องผ่าน Windows Firewall สร้างบัญชี **Master** พร้อมชุดเซอร์แวนต์และ
Craft Essence ครบทั้งหมด และตั้งการแสดงผลเป็นโหมดหน้าต่าง 1280x720 บนจอหลักของคุณ
จากนั้นกด **เล่น** เกมจะใช้เวลาราวหนึ่งนาทีกว่าจะถึงหน้าจอไตเติล

ถ้าสร้างกฎไฟร์วอลล์ไม่สำเร็จ ซึ่งนโยบายขององค์กรหรือชุดโปรแกรมความปลอดภัยอาจขัดขวางได้
Windows จะถามคุณเองแทนในครั้งแรกที่คุณกดเล่น หนึ่งครั้งสำหรับเซิร์ฟเวอร์ในเครื่อง
(`Server\python\python.exe`) และอีกหนึ่งครั้งสำหรับตัวเกม (`App\ago.exe`) ให้อนุญาตทั้งคู่
หน้าต่างที่ Windows ถามเหล่านั้นอาจเปิดขึ้น**ด้านหลัง**หน้าต่างเกม ดังนั้นถ้าเกมดูเหมือนค้างอยู่ที่จอดำ
ให้มองหาหน้าต่างเหล่านั้นในแถบงาน

การเริ่มครั้งต่อ ๆ ไปจะข้ามขั้นตอนเหล่านี้ทั้งหมดและไปที่หน้าเล่นทันที

**GUIDE_TH.md** ในโฟลเดอร์นี้คือคู่มือฉบับเต็ม ครอบคลุมความต้องการของระบบ วิธีได้เซอร์แวนต์ การควบคุม
ร้านแลกเปลี่ยน และการแก้ปัญหา ส่วน **GUIDE_EN.pdf** คือคู่มือฉบับภาษาอังกฤษ

สามเรื่องที่ควรรู้ก่อนเริ่ม

- **เกมทำงานจากไดรฟ์ E: หรือ Y: ไม่ได้** เพราะฮุกไฟล์ของตัวเกมเองส่งทุกพาธบนไดรฟ์เหล่านั้นไปยัง
  จุดเชื่อมข้อมูลของตู้ และเกมจะหยุดพร้อม ERROR 4104 ไดรฟ์อื่นใช้ได้หมด
- **เกมต้องทำงานในฐานะผู้ดูแลระบบ** นั่นคือหน้าต่างขออนุญาตของ Windows หนึ่งเดียวที่คุณข้ามไม่ได้
  ถ้าไม่ทำ เกมจะขึ้น ERROR 4105 ราวเก้าสิบวินาทีหลังบูต
- **เกมต้องใช้ซีพียูที่มี F16C** ซึ่ง Intel Core รุ่นที่ 3 (ปี 2012) ขึ้นไป และ Ryzen ทุกรุ่นมีให้
  ส่วนชิป Pentium และ Celeron รุ่นเก่าไม่มี เกมจะหยุดพร้อม 0xC000001D ตอนเริ่ม
  และไม่มีทางแก้บนซีพียูตัวนั้น

ถ้าคุณอยากติดตั้งไฟล์เองโดยไม่ผ่านตัวเรียกเกม ให้รัน `Apply-EN-Patch.ps1` ด้วยตัวเอง
สคริปต์นี้รับพารามิเตอร์ `-InstallRoot` และ `-Rollback` จะคืนทุกอย่างที่มันเขียนทับไป
เมื่อคุณพอใจกับการติดตั้งแล้ว จะลบ `payload\`, `Apply-EN-Patch.ps1` และ `manifest.json`
เพื่อเอาพื้นที่คืนก็ได้ แต่ควรเก็บไว้ถ้าคิดว่าอาจอยากย้อนกลับในภายหลัง

## สิ่งที่ใช้งานได้

- ข้อความในเกม แปลแล้ว 65,266 บรรทัด ทั้งเนื้อเรื่อง เควสต์ ประวัติเซอร์แวนต์ สกิล ไอเทม และเมนู
- งานภาพในเกม คลังสไปรต์ที่สร้างใหม่ 240 ชุด ทั้งหน้าไตเติล บทเรียนเริ่มต้น เทอร์มินัล การจัดทีม
  HUD การต่อสู้ หน้าผลลัพธ์ ร้านค้า การสังเคราะห์ กล่องของขวัญ ภารกิจมาสเตอร์ อันดับ หน้าช่วยเหลือ
  และตัวแก้ไขไตเติล
- ตัวเรียกเกม ทั้งหน้าเล่น บัญชี การ์ดและเด็ค การตั้งค่า และขั้นสูง เป็นภาษาไทยทั้งหมด
  พร้อมชื่อการ์ดภาษาอังกฤษอย่างเป็นทางการและเอฟเฟกต์ของ Craft Essence ในคลังการ์ด
- เล่นคนเดียวแบบออฟไลน์ ทั้งบทเรียนเริ่มต้น การออกรบเดี่ยว เทอร์มินัล ร้านแลกเปลี่ยน การสังเคราะห์
  My Room อันดับ และตัวแก้ไขไตเติล
- การซัมมอนในเกม ซึ่งสุ่มจากเซิร์ฟเวอร์ในเครื่องตามน้ำหนักที่ตั้งไว้ในหน้าอัตราการสุ่มของตัวเรียกเกม
  ทั้งนี้หน้าบัญชีก็มอบการ์ดให้ครบได้ในคลิกเดียวเช่นกัน

## สิ่งที่ยังใช้งานไม่ได้

- **ไม่มีการเล่นออนไลน์** ทุกอย่างทำงานกับเซิร์ฟเวอร์ในเครื่อง ไม่มีการจับคู่ผู้เล่น
  และไม่มีบริการอย่างเป็นทางการให้เชื่อมต่อ
- **หน้าจออีเวนต์บางหน้ายังเป็นภาษาญี่ปุ่น** ได้แก่ แบนเนอร์อีเวนต์ co-op หน้าจอผลลัพธ์ co-op
  และร้านค้าอีเวนต์รุ่นหลัง ๆ ทั้งหมดเป็นงานภาพ ไม่ใช่ข้อความ และส่วนที่เหลือของเกมไม่ได้รับผลกระทบ
- เกมถูกสร้างมาสำหรับการ์ด NVIDIA และต้องใช้ไดรเวอร์รุ่นปัจจุบัน จอดำมักหมายถึงไดรเวอร์ ไม่ใช่ตัวแพตช์
  บนกราฟิก AMD และ Intel ตัวเรียกเกมจะเปิดเลเยอร์ความเข้ากันได้ของ fluphus ที่แถมมาให้เอง
  (การตั้งค่า > การแสดงผล หัวข้อ "เลเยอร์ความเข้ากันได้สำหรับ AMD และ Intel")
  โดยเขาทดสอบบน RX 7900 XTX ที่ความละเอียด 1920x1080 เท่านั้น พร้อมจำกัดเฟรมเรตที่ 60 fps
## Using the launcher

- **Play.** Play plays on the server on this PC. The Status sentence says where things stand -
  whether the local server is running - or, in one sentence, why Play cannot start yet.
- **The Master menu.** Click the Master line on Play: it lists your local accounts to switch between,
  with New account at the bottom; Manage beside the line opens the accounts page.
- **Deck codes.** Copy code on Cards puts the deck on the clipboard as one line of text to paste in
  chat. Paste a code into the box beside it and press Import code: the deck loads and is saved as a
  new loadout, never over one you have.
- **Save a report.** Open the Status menu, the button at the end of the Status line on Play, and
  choose Save a report. It saves one zip, on the Desktop unless you pick another place, with your
  card number taken out. Nothing is uploaded: you attach the file yourself.
- **Controls.** Input picks keyboard, an Xbox or other controller, or a PlayStation 5 controller,
  with dead zone, rumble and a controller test. Bindings ties a key, a controller button or a
  combination such as Ctrl+F5 to a button on the game's screen: click a box and press what you want.
  Cursor sets the pointer and the mouse button that taps the screen. With Cloud23333's latest update
  the camera is his: Battles in free camera turns the view with the mouse in battle, and Controller
  camera turns it with the right stick, with a lock-on button. The controller methods of his update
  hide the mouse pointer in battle, story scenes included; choose Keyboard and mouse to use the mouse
  there. Changes count from the next Play.
- **PC play.** Settings > PC play: after a lost quest Continue and Withdraw wait for you, and popups
  stay open until you answer them; both are on. No GP purchase screen, Story pages wait for you and
  No menu countdown are off until they have been tried in the game; turn them on to try them.
  Changes count from the next Play.
- **Widescreen.** Settings > Display > Widescreen: Widescreen picture fits the picture to screens
  that are not 16:9, and Full width on taller screens keeps the whole width on 16:10 and 4:3; both
  are on. HUD and menus at the screen edges is off while it is being tried.
- **The other Settings cards.** Display also has a graphics card pick. Battle effects hides the dark
  or white screen during Noble Phantasms, hides the white screen flash, and has an Effect opacity
  slider for how strongly the game's own battle effects are drawn. Audio. Local server has the local
  server and its settings. Diagnostics and Help lists every game error code with its fix. The
  Operator menu keys (F1, F2) switch for ERROR 8404 is on the Controls page.
- **Pages of their own.** Photo Mode, Mods (the MOD folder of Cloud23333's latest update), Events
  (which limited-time events the server on this PC offers), Accounts and Battle Debug (battle test
  switches for local play, including Stop the battle timer) each have a page in the sidebar.
- **Logs.** Logs on Play opens the drawer, which shows one log at a time: Launcher, Game or Server.
  It remembers your choice.


## การรายงานปัญหา

Say which screen you were on and what you expected, and attach the zip from **Save a report** in
the Status menu on Play. It holds the logs from the `logs` folder next to `App`, among them:

| ไฟล์ | แสดงอะไร |
| --- | --- |
| `fgo-last-launch.log` | สคริปต์เปิดเกม ตั้งแต่เริ่มจนถึง exit code |
| `fgozh.log` | ฮุกไฟล์ภาษาไทย ว่าเกมโหลดไฟล์ใดไปบ้างจริง ๆ |
| `server-control.log`, `artemis-stderr.log`, `mariadb.log` | เซิร์ฟเวอร์ในเครื่องและฐานข้อมูลของมัน |
| `environment-check.txt` | การตรวจสอบสภาพแวดล้อม เขียนโดยหน้าการวินิจฉัยและความช่วยเหลือ |

**Settings > Diagnostics and Help** in the launcher lists the game's error codes with the fix for
each one; check it first, the answer is usually there.

## เครดิต

- **Cloud23333** เป็นผู้เขียน FGO Arcade local platform ทั้งแพ็กเกจเซิร์ฟเวอร์ ตัวเรียกเกม
  (`FGOLocalPlatform`) ที่ FGOAC scooby สร้างต่อยอดมา และฮุกไฟล์ที่แพตช์นี้ใช้โหลดภาษาไทยเข้าไป
  ทั้งหมดนี้เกิดขึ้นไม่ได้เลยหากไม่มีผลงานของเขา แพ็กเกจของเขาแจกฟรี ถ้ามีใครขายให้คุณ
  ให้ทวงเงินคืน
- **FGO Arcade wiki** และ **Atlas Academy** ถูกใช้เป็นแหล่งชื่อภาษาอังกฤษอย่างเป็นทางการของเซอร์แวนต์
  Craft Essence สกิล และไอเทม เกมและตัวเรียกเกมจึงเรียกทุกอย่างตามที่ฉบับภาษาอังกฤษเรียก
- FGO Arcade เป็นของ Sega และ TYPE-MOON นี่คืองานแปลโดยแฟนเกม ที่นำไปใช้กับไฟล์ที่คุณมีอยู่แล้ว
  และไม่มีการจำหน่าย
