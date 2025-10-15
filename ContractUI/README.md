# نظام إدارة العقود الرقمية - Contract Management UI

واجهة ويب بسيطة لإدارة العقود الرقمية عبر منصة صادق (Sadeq Digital Signature Platform)

## 📋 المحتويات

- `index.html` - صفحة قائمة العقود ومتابعة حالاتها
- `create-contract.html` - صفحة إنشاء عقود جديدة (قالب أو PDF)
- `styles.css` - ملف التنسيقات المشتركة

## 🚀 كيفية الاستخدام

### الطريقة 1: استخدام VS Code Live Server (موصى بها)

1. افتح المجلد `ContractUI` في VS Code
2. انقر بالزر الأيمن على `index.html`
3. اختر "Open with Live Server"
4. سيتم فتح الصفحة في المتصفح على `http://127.0.0.1:5500` أو منفذ مشابه

### الطريقة 2: استخدام Python HTTP Server

```bash
cd ContractUI
python -m http.server 8000
```

ثم افتح المتصفح على: `http://localhost:8000`

### الطريقة 3: استخدام Node.js HTTP Server

```bash
cd ContractUI
npx http-server -p 8000
```

ثم افتح المتصفح على: `http://localhost:8000`

### الطريقة 4: فتح الملف مباشرة

يمكنك فتح `index.html` مباشرة في المتصفح، لكن تأكد من تشغيل Azure Functions أولاً.

## ⚙️ الإعدادات المطلوبة

### 1. تشغيل Azure Functions

تأكد من تشغيل Azure Functions محلياً:

```bash
cd /workspaces/functions
func start
```

الوظائف ستعمل على: `http://localhost:7071`

### 2. تحديث عنوان API (إذا لزم الأمر)

إذا كانت الوظائف تعمل على منفذ مختلف، قم بتحديث `API_BASE_URL` في الملفات:

**في `index.html`:**
```javascript
const API_BASE_URL = 'http://localhost:7071/api';
```

**في `create-contract.html`:**
```javascript
const API_BASE_URL = 'http://localhost:7071/api';
```

### 3. إعداد قاعدة البيانات

تأكد من تشغيل SQL Script لإنشاء الجدول:

```sql
-- تشغيل ملف ContractStatusTable.sql
```

## 📱 الصفحات المتاحة

### 1. قائمة العقود (`index.html`)

- **عرض جميع العقود** مع حالاتها
- **إحصائيات سريعة**:
  - إجمالي العقود
  - العقود المرسلة بنجاح
  - العقود الموقعة
  - العقود بانتظار التوقيع
- **فلترة وبحث** في العقود
- **تحديث تلقائي** للبيانات

### 2. إنشاء عقد جديد (`create-contract.html`)

#### نوع 1: عقد من قالب موجود
- رمز الشركة (إجباري)
- اسم الشركة (إجباري)
- البريد الإلكتروني (إجباري)
- رقم الهاتف (إجباري، يبدأ بـ +966)
- رقم الهوية الوطنية (إجباري، 10 أرقام)
- معرف القالب من منصة صادق (إجباري)
- الفروع/الأجهزة (اختياري)

#### نوع 2: رفع عقد PDF
- نفس البيانات الأساسية
- رفع ملف PDF (حجم أقصى: 10 ميجابايت)

## 🔑 متطلبات البيانات

### رقم الهاتف
- يجب أن يبدأ بـ `+966` للأرقام السعودية
- مثال: `+966501234567`

### رقم الهوية الوطنية
- يجب أن يكون 10 أرقام فقط
- مثال: `1234567890`

### رمز الشركة
- رمز فريد لكل شركة
- لا يمكن تكرار نفس الرمز (UNIQUE Constraint)
- مثال: `COMP001`

### معرف القالب
- يتم الحصول عليه من لوحة تحكم منصة صادق
- مثال: `bce984ff-50bd-4792-ba5d-1da96de29d2c`

## 🎨 المميزات

- ✅ واجهة باللغة العربية (RTL)
- ✅ تصميم متجاوب (Responsive)
- ✅ دعم نوعين من العقود (قالب / PDF)
- ✅ تحقق من صحة البيانات
- ✅ رسائل خطأ ونجاح واضحة
- ✅ عرض حالة العقد في الوقت الفعلي
- ✅ إحصائيات وتقارير سريعة

## 🐛 حل المشكلات

### مشكلة CORS

إذا ظهرت رسالة خطأ CORS:

1. تأكد من أن `host.json` يحتوي على:
```json
"Access-Control-Allow-Origin": "*"
```

2. أعد تشغيل Azure Functions:
```bash
func start
```

### عدم ظهور البيانات

1. تأكد من تشغيل Azure Functions على `http://localhost:7071`
2. تأكد من إنشاء جدول `IntegrationSadeqContracts` في قاعدة البيانات
3. افتح Console في المتصفح لمشاهدة الأخطاء (F12)

### مشكلة رفع الملفات

1. تأكد من حجم الملف أقل من 10 ميجابايت
2. تأكد من أن الملف بصيغة PDF فقط
3. تحقق من صلاحيات القراءة/الكتابة

## 📊 API Endpoints المستخدمة

### GET /api/contracts
جلب جميع العقود من قاعدة البيانات

**Response:**
```json
{
  "success": true,
  "count": 5,
  "data": [...]
}
```

### POST /api/sadeq_request
إنشاء عقد من قالب

**Request Body:**
```json
{
  "companyCode": "COMP001",
  "destinationName": "شركة كراج",
  "destinationEmail": "info@karage.co",
  "destinationPhoneNumber": "+966501234567",
  "nationalId": "1234567890",
  "templateId": "template-id",
  "terminals": "T001, T002"
}
```

### POST /api/sadeq_upload_pdf
رفع عقد PDF

**Request:** multipart/form-data
- file: PDF file
- companyCode: string
- companyName: string
- email: string
- phoneNumber: string
- nationalId: string
- terminals: string (optional)

## 🔒 الأمان

- تأكد من تغيير `Access-Control-Allow-Origin` من `*` إلى النطاق الفعلي في الإنتاج
- استخدم HTTPS في الإنتاج
- لا تشارك بيانات الاعتماد (credentials) في الكود

## 📝 ملاحظات

- صلاحية العقد: 30 يوماً من تاريخ الإرسال
- يتم إرسال إشعارات للعميل عبر البريد الإلكتروني والرسائل القصيرة
- يمكن متابعة حالة التوقيع من صفحة قائمة العقود

## 🚀 النشر للإنتاج

عند النشر للإنتاج:

1. قم بتحديث `API_BASE_URL` في الملفات:
```javascript
const API_BASE_URL = 'https://your-function-app.azurewebsites.net/api';
```

2. قم بتحديث CORS في `host.json`:
```json
"Access-Control-Allow-Origin": "https://your-domain.com"
```

3. ارفع ملفات HTML إلى:
   - Azure Static Web Apps
   - Azure Blob Storage (Static Website)
   - أي خدمة استضافة أخرى

## 📞 الدعم

للمساعدة أو الاستفسارات، تواصل مع فريق التطوير.
