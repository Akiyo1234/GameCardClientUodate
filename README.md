# GameCardClient (Multiplayer Card Game)

โปรเจคเกมการ์ด Multiplayer (สไตล์ Splendor) พัฒนาด้วย Unity และ Photon Fusion

## 📋 ความต้องการของระบบ (Requirements)
* **Unity Version:** `6000.3.9f1` (Unity 6) หรือเวอร์ชันที่แนะนำ
* **รูปแบบการเล่น:** Multiplayer ผ่านเครือข่าย

## 🚀 การติดตั้งและตั้งค่าโปรเจค (Setup Guide)

### 1. การติดตั้ง Unity
1. เปิด **Unity Hub**
2. ไปที่แท็บ **Installs** -> กด **Install Editor**
3. เลือกเวอร์ชัน `6000.3.9f1` (หากไม่มีให้เลือกให้ติดตั้งผ่านแท็บ Archive หรือใช้ Unity Version Control)
4. เพิ่มโปรเจค `GameCardClient` ลงใน Unity Hub และเปิดโปรเจค

### 2. การตั้งค่าระบบเครือข่าย (Photon Fusion Server)
เกมนี้ใช้ Photon Fusion สำหรับระบบ Multiplayer
1. ไปที่โฟลเดอร์ใน Unity: `Assets/Photon/Fusion/Resources`
2. หาไฟล์ `NetworkProjectConfig`
3. ในช่อง **App Id Fusion** จะต้องใส่ App ID ที่ได้จาก Photon Dashboard 
   *(หากทีมงานสร้าง App ID ไว้แล้วสามารถข้ามขั้นตอนนี้ได้ หากยังไม่มีต้องสมัครที่ dashboard.photonengine.com แล้วนำ App ID มาใส่)*

### 3. การตั้งค่าฐานข้อมูล (Database / Backend)
*(แก้ไขส่วนนี้ตามที่ทีมงานใช้ หากมีการต่อ Backend เพิ่มเติม)*
* **Supabase / Firebase / Custom Server:** หากมีการเรียกใช้ API เพื่อดึงข้อมูลผู้เล่น ให้แน่ใจว่าได้ระบุ `Base URL` หรือ `API Key` ลงในสคริปต์ที่เป็น Config หรือ Network Manager แล้ว

## 🎮 โครงสร้างโค้ด (Project Structure)
* `Assets/Scripts/Network` - จัดการการเชื่อมต่อและ state ของผู้เล่นส่วนกลาง (FusionManager)
* `Assets/Scripts/Models` - รูปแบบข้อมูลภายในเกม เช่น CardData, NobleData, RoomData
* `Assets/Scripts/UI` - ระบบหน้าจอและการอัปเดตสถานะของส่วนติดต่อผู้ใช้ เช่น CardDisplay, NobleDisplay

## ⚙️ วิธีการทดสอบเกม (Testing)
1. เปิด Scene หลักของเกม
2. ไปที่เมนูต้านบน `Fusion` > **Network Project Config** เพื่อดูสถานะ
3. สามารถรันหลาย Instance พร้อมกันได้ใน Editor โดยไปที่โฟลเดอร์ `Tools` หรือใช้ระบบ ParrelSync เพื่อเปิด Client แยกทดสอบการเชื่อมต่อ
