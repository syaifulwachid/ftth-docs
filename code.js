// ==========================================
// CONFIGURATION (PRE-CONFIGURED)
// ==========================================
var BOT_TOKEN = "8935550153:AAGdDTTsG4zEi-IoJtGhr-lvb75kNYI3aHw";     
var ADMIN_TELEGRAM_ID = "247653844";    

// RSA Private Key (PKCS#8) generated during setup
var PRIVATE_KEY_PEM = 
"-----BEGIN PRIVATE KEY-----\n" +
"MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQDN3jfsz1YeNIJb\n" +
"b7fqt6pCiVuHxLSH/QgObnZqaDkdbnAYISaGOKrRM/J0FsKXnRB7razgOiQQg32H\n" +
"K9KzjLgCjPhpxTHHbHIqegVQN1f3U0Qm5rWrskFrrTQTg6HC6rrMuwsr38ZusJjp\n" +
"wCQ6PnqOB80IJ1JWk4OGGTQfs68XIIMCbU1Kom3KoJqsMNfQztKjOTJH5uiBga5I\n" +
"RcqY0yfAecRcyAyfpAwW11Izoz1YAxVKQ5k65snEEwrEfakRfC2tA6292AxdljAT\n" +
"wupO0SmkP3Co6MOlPNj//78dvCL2vCjASmTCEJsDjkVRWm2brYxXKASArtRGAw1I\n" +
"TvBm6P8tAgMBAAECggEADcNtDr5/3e4Zxv4tmBomXmNrhKSwgyT2DGzzKsMECoUv\n" +
"JlXVCwUv2mO0MnGxGczM5M/kLmuErLv1wPs7j3h5duYw13VxEgmcil11DHtevLDK\n" +
"7iTfgXad7BJ82E8lGfByg6x/nzPLQuw4lOwdH+28aNF5sjFazmH3WZhGxVXQf2W0\n" +
"yKFP+HB2g9D1meqGbhKlPgN8ztmm9Mi8cFvoH26klFnTgMJNwefIEmFWxEyaaWSB\n" +
"yPEC+KtvclNE4XAoXREi9tRvhdYnNTM9DGp7mBsiRYh4Rt7UB0vt/M0Bs2lStnYW\n" +
"gSaV94VrC4rHqbTBo8fnA/WN1xMgthlcTgRD44nmgQKBgQD1emlOiJKkuS3kpkku\n" +
"h7u6kTDe3Qh5OBPOMhWPYvh56q+W2c3CkfWI7YN45aE3oYq2fswmtlspFZdulImG\n" +
"SvOXHumU4brPawj1ajboXUotXB09tzu/1oz3yBIKgPcICDOk/1nvk+R81evq3UYN\n" +
"bGVLAzXFwgqHT3A/a5pTJmY1QQKBgQDWsSwSDbqoxUoUXIuloMUTLrT9TgmJSnDB\n" +
"JM3mHsKEws5sHJhGtfrR6YeqWaR/LwpA2o2CQNzVxuSXnizDfxOkyL1hnYcxl4ZZ\n" +
"cc4lm7aU8WnBKnYR2hU0o7LIA90kDx5EK65XFX5STEKqlmkTSr2eYbso4qKPcL25\n" +
"iVrJnCMy7QKBgATjWB95NrpS+af467Iif8l6RKfbbOTFChfsBWPii6IZ2z88vQ0n\n" +
"zOTaHekVYX1zGQkDQ1tt/Ci4RlisWoSzD2Ct++a8C/U/Y2FHqSo9WVHH6Mkm0ejD\n" +
"A/GXKUzOPp0JVMXvU8IihsU5mUYG+/MeenHg8XwrnfwNx+VrZhpLxHNBAoGARdNb\n" +
"1QdYFToNbO/oj1bpoeKIBPaTjW6Dm53fxZ9tfoZpYqouMJlRWWJNuG7tXFwtRoiO\n" +
"i7WS3YiRompUfsTe27JaPdxhMxToIkEsXfj1+h1GWwf3XLkEOpmfNQRksSylmGBo\n" +
"lHQuIJAjAp5m0Fp3r4Jzv8luO57cZfKxb27z18UCgYEAvVcm0P5kQiW74K3lXSF1\n" +
"ZZAXI/irJckywwBImoJxvugVaos/qRJ2GJCn/XB4rxpZYsXemcYrzEsSaGlZ4ZDC\n" +
"QqaCr9qs1gQ8pT1pweHEBtVH4kwPiitglsJW/tbbhb45eiMk2qc8y8qETRe9ytPX\n" +
"OUJlAoZXoiPUqOzSHqmhrmc=\n" +
"-----END PRIVATE KEY-----";

// ==========================================
// SYSTEM SETUP
// ==========================================
function setup() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  
  // Users sheet
  var usersSheet = ss.getSheetByName("Users");
  if (!usersSheet) {
    usersSheet = ss.insertSheet("Users");
    usersSheet.appendRow(["deviceId", "telegram", "status", "expiresAt", "lastUsedSalt", "registeredAt"]);
  }
  
  // Payments sheet
  var paymentsSheet = ss.getSheetByName("Payments");
  if (!paymentsSheet) {
    paymentsSheet = ss.insertSheet("Payments");
    paymentsSheet.appendRow(["paymentId", "deviceId", "telegram", "amount", "status", "requestTime", "key", "durationMonths"]);
  } else {
    // If sheet exists, ensure durationMonths column header is there
    var headers = paymentsSheet.getRange(1, 1, 1, paymentsSheet.getLastColumn()).getValues()[0];
    if (headers.indexOf("durationMonths") === -1) {
      paymentsSheet.getRange(1, 8).setValue("durationMonths");
    }
  }
  
  // Telemetry sheet
  var telemetrySheet = ss.getSheetByName("Telemetry");
  if (!telemetrySheet) {
    telemetrySheet = ss.insertSheet("Telemetry");
    telemetrySheet.appendRow(["deviceId", "projectsCreated", "lastActive", "version"]);
  }

  // ExportTelemetry sheet
  var exportSheet = ss.getSheetByName("ExportTelemetry");
  if (!exportSheet) {
    exportSheet = ss.insertSheet("ExportTelemetry");
    exportSheet.appendRow(["deviceId", "fileName", "totalRouteLength", "totalHompass", "totalPoles", "timestamp", "version", "filePath"]);
  }

  // Config sheet
  var configSheet = ss.getSheetByName("Config");
  if (!configSheet) {
    configSheet = ss.insertSheet("Config");
    configSheet.appendRow(["Key", "Value"]);
    configSheet.appendRow(["BasePrice", 100000]);
    configSheet.appendRow(["BasePrice_1M_Trial", 30000]);
    configSheet.appendRow(["BasePrice_1M", 100000]);
    configSheet.appendRow(["BasePrice_3M", 270000]);
    configSheet.appendRow(["BasePrice_6M", 500000]);
    configSheet.appendRow(["BasePrice_12M", 900000]);
  } else {
    // Ensure all pricing tiers exist
    var data = configSheet.getDataRange().getValues();
    var keys = data.map(function(row) { return row[0]; });
    var defaults = [
      ["BasePrice_1M_Trial", 30000],
      ["BasePrice_1M", 100000],
      ["BasePrice_3M", 270000],
      ["BasePrice_6M", 500000],
      ["BasePrice_12M", 900000]
    ];
    for (var i = 0; i < defaults.length; i++) {
      if (keys.indexOf(defaults[i][0]) === -1) {
        configSheet.appendRow(defaults[i]);
      }
    }
  }
  
  Logger.log("Sheets initialized successfully!");
}

// Set Webhook link in Telegram (Hardcoded Web App URL)
function setWebhook() {
  var webAppUrl = "https://script.google.com/macros/s/AKfycbxgtBcdELeHFXWibuieFIov1OUQcXowq0XPgYoSrMA4wORqgdzG6K4UbDqhUAIl_B0u9w/exec";
  var url = "https://api.telegram.org/bot" + BOT_TOKEN + "/setWebhook?url=" + encodeURIComponent(webAppUrl);
  var response = UrlFetchApp.fetch(url);
  Logger.log("Set Webhook result: " + response.getContentText());
}

// ==========================================
// WEB API ENDPOINTS (GET & POST)
// ==========================================

function doGet(e) {
  var action = e.parameter.action;
  
  if (action === "getPrice") {
    var ss = SpreadsheetApp.getActiveSpreadsheet();
    var configSheet = ss.getSheetByName("Config");
    var prices = {
      "BasePrice_1M": 100000,
      "BasePrice_3M": 270000,
      "BasePrice_6M": 500000,
      "BasePrice_12M": 900000
    };
    if (configSheet) {
      var data = configSheet.getDataRange().getValues();
      var foundAny = false;
      for (var i = 1; i < data.length; i++) {
        var key = data[i][0];
        if (key && key.indexOf("BasePrice") === 0) {
          var val = parseInt(data[i][1]);
          if (!isNaN(val)) {
            prices[key] = val;
            foundAny = true;
          }
        }
      }
      if (foundAny && !prices["BasePrice_1M"] && prices["BasePrice"]) {
        prices["BasePrice_1M"] = prices["BasePrice"];
        prices["BasePrice_3M"] = Math.round(prices["BasePrice"] * 3 * 0.9);
        prices["BasePrice_6M"] = Math.round(prices["BasePrice"] * 6 * 0.85);
        prices["BasePrice_12M"] = Math.round(prices["BasePrice"] * 12 * 0.75);
      }
    }

    // Check if this device has already used or requested the Trial package
    var deviceId = e.parameter.deviceId;
    if (deviceId) {
      var paymentsSheet = ss.getSheetByName("Payments");
      if (paymentsSheet) {
        var payData = paymentsSheet.getDataRange().getValues();
        var trialPrice = prices["BasePrice_1M_Trial"] || 30000;
        for (var j = 1; j < payData.length; j++) {
          var payDeviceId = payData[j][1];
          var payAmount = parseFloat(payData[j][3]);
          var payStatus = payData[j][4];
          if (payDeviceId === deviceId && payAmount >= trialPrice && payAmount < (trialPrice + 1000) && (payStatus === "Approved" || payStatus === "Pending")) {
            delete prices["BasePrice_1M_Trial"];
            break;
          }
        }
      }
    }

    return ContentService.createTextOutput(JSON.stringify(prices))
      .setMimeType(ContentService.MimeType.JSON);
  }
  
  if (action === "requestActivation") {
    var ss = SpreadsheetApp.getActiveSpreadsheet();
    var paymentId = e.parameter.paymentId;
    var deviceId = e.parameter.deviceId;
    var telegram = e.parameter.telegram || "AutoCAD User";
    var duration = parseInt(e.parameter.duration || "1");
    var amount = parseInt(e.parameter.amount);
    
    var paymentsSheet = ss.getSheetByName("Payments");
    if (paymentsSheet) {
      var headers = paymentsSheet.getRange(1, 1, 1, paymentsSheet.getLastColumn()).getValues()[0];
      if (headers.indexOf("durationMonths") === -1) {
        paymentsSheet.getRange(1, 8).setValue("durationMonths");
      }
      
      var payData = paymentsSheet.getDataRange().getValues();
      var rowIdx = -1;
      for (var k = 1; k < payData.length; k++) {
        if (payData[k][0] === paymentId) {
          rowIdx = k + 1;
          break;
        }
      }
      
      if (rowIdx === -1) {
        paymentsSheet.appendRow([paymentId, deviceId, telegram, amount, "Pending", new Date(), "", duration]);
      } else {
        paymentsSheet.getRange(rowIdx, 2).setValue(deviceId);
        paymentsSheet.getRange(rowIdx, 3).setValue(telegram);
        paymentsSheet.getRange(rowIdx, 4).setValue(amount);
        paymentsSheet.getRange(rowIdx, 5).setValue("Pending");
        paymentsSheet.getRange(rowIdx, 6).setValue(new Date());
        paymentsSheet.getRange(rowIdx, 8).setValue(duration);
      }
    }
    
    // Notify Admin via Telegram with application label
    var adminMsg = "🔔 **📱 Aplikasi: FTTH Basemap**\n" +
                   "**Permintaan Aktivasi Baru!**\n\n" +
                   "• User: " + telegram + "\n" +
                   "• Device ID: `" + deviceId + "`\n" +
                   "• Paket: **" + duration + " Bulan**\n" +
                   "• Payment ID: `" + paymentId + "`\n" +
                   "• Nominal: **Rp " + amount.toLocaleString("id-ID") + "**\n" +
                   "• Waktu: " + new Date().toLocaleString("id-ID") + "\n\n" +
                   "Admin, silakan verifikasi mutasi rekening. Jika dana masuk, klik Approve:";
    
    var inlineKeyboard = {
      inline_keyboard: [
        [
          { text: "✅ Approve", callback_data: "approve:" + paymentId + ":0" },
          { text: "❌ Reject", callback_data: "reject:" + paymentId + ":0" }
        ]
      ]
    };
    sendTelegramMessage(ADMIN_TELEGRAM_ID, adminMsg, inlineKeyboard);
    
    return ContentService.createTextOutput(JSON.stringify({ Status: "Success", Message: "Request sent to Admin." }))
      .setMimeType(ContentService.MimeType.JSON);
  }
  
  if (action === "checkLicense") {
    var ss = SpreadsheetApp.getActiveSpreadsheet();
    var deviceId = e.parameter.deviceId;
    var usersSheet = ss.getSheetByName("Users");
    var data = usersSheet.getDataRange().getValues();
    
    for (var i = 1; i < data.length; i++) {
      if (data[i][0] === deviceId) {
        var status = data[i][2]; // "Active" or "Revoked"
        var expiresAt = new Date(data[i][3]);
        
        if (status === "Revoked") {
          return ContentService.createTextOutput(JSON.stringify({
            Status: "Revoked",
            Message: "Lisensi dinonaktifkan oleh Administrator."
          })).setMimeType(ContentService.MimeType.JSON);
        }
        
        if (new Date() > expiresAt) {
          return ContentService.createTextOutput(JSON.stringify({
            Status: "Expired",
            Message: "Masa aktif lisensi telah habis.",
            ExpirationDate: data[i][3]
          })).setMimeType(ContentService.MimeType.JSON);
        }
        
        var expiresStr = new Date(data[i][3]).toISOString();
        var txSalt = data[i][4];
        var activationKey = generateActivationKey(deviceId, txSalt, expiresStr);

        return ContentService.createTextOutput(JSON.stringify({
          Status: "Active",
          Message: "Lisensi Aktif.",
          ExpirationDate: data[i][3],
          ActivationKey: activationKey
        })).setMimeType(ContentService.MimeType.JSON);
      }
    }
    
    return ContentService.createTextOutput(JSON.stringify({
      Status: "Inactive",
      Message: "Perangkat belum terdaftar."
    })).setMimeType(ContentService.MimeType.JSON);
  }
  
  if (action === "checkPayment") {
    var ss = SpreadsheetApp.getActiveSpreadsheet();
    var paymentId = e.parameter.paymentId;
    var clientDeviceId = e.parameter.deviceId;
    var paymentsSheet = ss.getSheetByName("Payments");
    var data = paymentsSheet.getDataRange().getValues();
    
    for (var i = 1; i < data.length; i++) {
      if (data[i][0] === paymentId) {
        if (clientDeviceId && (data[i][1] === "Unknown" || !data[i][1])) {
          paymentsSheet.getRange(i + 1, 2).setValue(clientDeviceId);
        }
        var status = data[i][4]; // "Pending", "Approved", "Rejected"
        var key = data[i][6];
        
        if (status === "Approved") {
          return ContentService.createTextOutput(JSON.stringify({
            Status: "Approved",
            ActivationKey: key
          })).setMimeType(ContentService.MimeType.JSON);
        } else {
          return ContentService.createTextOutput(JSON.stringify({
            Status: status
          })).setMimeType(ContentService.MimeType.JSON);
        }
      }
    }
    
    return ContentService.createTextOutput(JSON.stringify({ Status: "Pending" }))
      .setMimeType(ContentService.MimeType.JSON);
  }
  
  return ContentService.createTextOutput("FTTH Basemap API Active");
}

function doPost(e) {
  try {
    var jsonString = e.postData.contents;
    var json = JSON.parse(jsonString);
    
    // Check if it is Export Telemetry
    if (json.deviceId && json.exportTelemetry === true) {
      var ss = SpreadsheetApp.getActiveSpreadsheet();
      var exportSheet = ss.getSheetByName("ExportTelemetry");
      if (!exportSheet) {
        exportSheet = ss.insertSheet("ExportTelemetry");
        exportSheet.appendRow(["deviceId", "fileName", "totalRouteLength", "totalHompass", "totalPoles", "timestamp", "version", "filePath"]);
      }
      
      var deviceId = json.deviceId;
      var fileName = json.fileName;
      var filePath = json.filePath;
      var totalRouteLength = json.totalRouteLength || 0;
      var totalHompass = json.totalHompass || 0;
      var totalPoles = json.totalPoles || 0;
      var version = json.version || "4.0";
      
      exportSheet.appendRow([deviceId, fileName, totalRouteLength, totalHompass, totalPoles, new Date(), version, filePath]);
      
      return ContentService.createTextOutput(JSON.stringify({ Status: "Success" }))
        .setMimeType(ContentService.MimeType.JSON);
    }
    
    // Check if it is Telemetry API
    if (json.deviceId && json.projectsCreated !== undefined) {
      var ss = SpreadsheetApp.getActiveSpreadsheet();
      var deviceId = json.deviceId;
      var addedProjects = parseInt(json.projectsCreated);
      var version = json.version || "4.0";
      
      // Update Telemetry sheet
      var telSheet = ss.getSheetByName("Telemetry");
      var telData = telSheet.getDataRange().getValues();
      var foundTel = false;
      
      for (var i = 1; i < telData.length; i++) {
        if (telData[i][0] === deviceId) {
          var currentVal = parseInt(telData[i][1]);
          telSheet.getCell(i + 1, 2).setValue(currentVal + addedProjects);
          telSheet.getCell(i + 1, 3).setValue(new Date());
          telSheet.getCell(i + 1, 4).setValue(version);
          foundTel = true;
          break;
        }
      }
      
      if (!foundTel) {
        telSheet.appendRow([deviceId, addedProjects, new Date(), version]);
      }
      
      // Check if user is revoked
      var usersSheet = ss.getSheetByName("Users");
      var userData = usersSheet.getDataRange().getValues();
      for (var j = 1; j < userData.length; j++) {
        if (userData[j][0] === deviceId) {
          if (userData[j][2] === "Revoked") {
            return ContentService.createTextOutput(JSON.stringify({ Status: "Revoked" }))
              .setMimeType(ContentService.MimeType.JSON);
          }
          break;
        }
      }
      
      return ContentService.createTextOutput(JSON.stringify({ Status: "Success" }))
        .setMimeType(ContentService.MimeType.JSON);
    }
    
    // Process Telegram Webhook Update
    handleTelegramUpdate(json);
  } catch(err) {
    Logger.log("Error in doPost: " + err.message);
  }
  
  return HtmlService.createHtmlOutput("OK");
}

// ==========================================
// TELEGRAM BOT WEBHOOK HANDLER
// ==========================================
function handleTelegramUpdate(update) {
  if (update.message) {
    var msg = update.message;
    var chatId = msg.chat.id;
    var text = msg.text ? msg.text.trim() : "";
    var user = msg.from.username ? "@" + msg.from.username : msg.from.first_name;
    
    if (text.indexOf("/start") === 0) {
      var greeting = "👋 Selamat datang di Bot Lisensi FTTH Basemap!\n\n" +
                     "Bot ini digunakan untuk memverifikasi pembayaran langganan bulanan software FTTH Basemap.\n\n" +
                     "💡 **Cara Membeli/Perpanjang Langganan:**\n" +
                     "1. Buka software FTTH Basemap di AutoCAD.\n" +
                     "2. Klik tombol kunci (🔑) untuk melihat nominal unik dan Payment ID Anda.\n" +
                     "3. Transfer nominal presisi ke QRIS.\n" +
                     "4. Ketik /pay `Payment_ID` di bot ini.\n\n" +
                     "Contoh: `/pay FTTH-248-ABC123`";
      sendTelegramMessage(chatId, greeting);
      return;
    }
    
    if (text.indexOf("/pay") === 0) {
      var parts = text.split(" ");
      if (parts.length < 2) {
        // Abaikan command jika format kosong agar tidak mengganggu bot bersama
        return;
      }
      
      var paymentId = parts[1].trim();
      var idParts = paymentId.split("-");
      if (idParts.length !== 3 || idParts[0] !== "FTTH") {
        // Bukan untuk program FTTH Basemap, abaikan agar tidak mengganggu program lain
        return;
      }
      
      var suffixStr = idParts[1]; // e.g. "248"
      var txSalt = idParts[2];   // e.g. "ABC123"
      var suffix = parseInt(suffixStr);
      
      // Ambil BasePrice dinamis dari sheet Config
      var ss = SpreadsheetApp.getActiveSpreadsheet();
      var configSheet = ss.getSheetByName("Config");
      var basePrice = 100000; // default fallback
      if (configSheet) {
        var configData = configSheet.getDataRange().getValues();
        for (var i = 1; i < configData.length; i++) {
          if (configData[i][0] === "BasePrice") {
            basePrice = parseInt(configData[i][1]);
            break;
          }
        }
      }
      var amount = basePrice + suffix;
      
      // Save Pending payment in Google Sheet (default 1 month for Telegram /pay command)
      var paymentsSheet = ss.getSheetByName("Payments");
      var payData = paymentsSheet.getDataRange().getValues();
      var deviceId = "Unknown";
      
      var alreadyLogged = false;
      for (var k = 1; k < payData.length; k++) {
        if (payData[k][0] === paymentId) {
          alreadyLogged = true;
          break;
        }
      }
      
      if (!alreadyLogged) {
        // Ensure durationMonths column
        var headers = paymentsSheet.getRange(1, 1, 1, paymentsSheet.getLastColumn()).getValues()[0];
        if (headers.indexOf("durationMonths") === -1) {
          paymentsSheet.getRange(1, 8).setValue("durationMonths");
        }
        paymentsSheet.appendRow([paymentId, deviceId, user, amount, "Pending", new Date(), "", 1]);
      }
      
      // Respond to user
      var reply = "📥 **Permintaan Pembayaran Diterima!**\n\n" +
                  "Silahkan selesaikan pembayaran sebesar:\n" +
                  "💰 **Rp " + amount.toLocaleString("id-ID") + "**\n\n" +
                  "Pastikan nominal transfer sama persis. " +
                  "Begitu pembayaran masuk ke rekening/e-wallet, Admin akan segera memverifikasi.";
      sendTelegramMessage(chatId, reply);
      
      // Notify Admin with App Tagging
      var adminMsg = "🔔 **📱 Aplikasi: FTTH Basemap**\n" +
                     "**Tagihan Baru Diminta!**\n\n" +
                     "• User: " + user + " (ID: " + chatId + ")\n" +
                     "• Payment ID: `" + paymentId + "`\n" +
                     "• Nominal: **Rp " + amount.toLocaleString("id-ID") + "**\n" +
                     "• Waktu: " + new Date().toLocaleString("id-ID") + "\n\n" +
                     "Admin, silakan verifikasi mutasi rekening. Jika dana masuk, klik tombol di bawah:";
      
      var inlineKeyboard = {
        inline_keyboard: [
          [
            { text: "✅ Approve", callback_data: "approve:" + paymentId + ":" + chatId },
            { text: "❌ Reject", callback_data: "reject:" + paymentId + ":" + chatId }
          ]
        ]
      };
      sendTelegramMessage(ADMIN_TELEGRAM_ID, adminMsg, inlineKeyboard);
      return;
    }
    
    // Admin Commands
    if (chatId.toString() === ADMIN_TELEGRAM_ID.toString()) {
      if (text === "/stats") {
        var stats = getStatsSummary();
        sendTelegramMessage(chatId, stats);
        return;
      }
      
      if (text === "/users" || text === "/list") {
        sendUsersListMenu(chatId);
        return;
      }
      
      if (text.indexOf("/keygen_ftth") === 0) {
        var keyParts = text.split(" ");
        if (keyParts.length < 3) {
          sendTelegramMessage(chatId, "⚠️ Format salah! Gunakan: `/keygen_ftth <deviceId> <durationMonths>`\nContoh: `/keygen_ftth ABCDE12345 3`");
          return;
        }
        
        var devId = keyParts[1].trim();
        var durationVal = parseInt(keyParts[2].trim());
        if (isNaN(durationVal) || durationVal <= 0) {
          sendTelegramMessage(chatId, "⚠️ Durasi bulan harus berupa angka positif!");
          return;
        }
        
        var ss = SpreadsheetApp.getActiveSpreadsheet();
        var usersSheet = ss.getSheetByName("Users");
        var userData = usersSheet.getDataRange().getValues();
        var userRowIdx = -1;
        for (var j = 1; j < userData.length; j++) {
          if (userData[j][0] === devId) {
            userRowIdx = j + 1;
            break;
          }
        }
        
        var expiresAt = new Date();
        if (userRowIdx !== -1) {
          var currentExpiry = new Date(userData[userRowIdx - 1][3]);
          if (userData[userRowIdx - 1][2] === "Active" && currentExpiry > expiresAt) {
            expiresAt = currentExpiry;
          }
        }
        
        expiresAt.setMonth(expiresAt.getMonth() + durationVal);
        var expiresStr = expiresAt.toISOString();
        
        var txSalt = "MANUAL";
        var activationKey = generateActivationKey(devId, txSalt, expiresStr);
        
        // Save to Users sheet
        if (userRowIdx !== -1) {
          usersSheet.getRange(userRowIdx, 2).setValue("Generated Manually");
          usersSheet.getRange(userRowIdx, 3).setValue("Active");
          usersSheet.getRange(userRowIdx, 4).setValue(expiresStr);
          usersSheet.getRange(userRowIdx, 5).setValue(txSalt);
        } else {
          usersSheet.appendRow([devId, "Generated Manually", "Active", expiresStr, txSalt, new Date()]);
        }
        
        var responseMsg = "🔑 **📱 Aplikasi: FTTH Basemap**\n" +
                          "**Serial Key Berhasil Dibuat!**\n\n" +
                          "• Device ID: `" + devId + "`\n" +
                          "• Durasi: **" + durationVal + " Bulan**\n" +
                          "• Aktif s/d: **" + expiresAt.toLocaleString("id-ID") + "**\n\n" +
                          "Copy Serial Key di bawah ini untuk pengguna:\n" +
                          "`" + activationKey + "`";
        sendTelegramMessage(chatId, responseMsg);
        return;
      }
    }
  } 
  
  else if (update.callback_query) {
    var cb = update.callback_query;
    var cbData = cb.data;
    var cbChatId = cb.message.chat.id;
    var msgId = cb.message.message_id;
    
    // Authorize Admin for callbacks
    if (cbChatId.toString() !== ADMIN_TELEGRAM_ID.toString()) {
      answerCallbackQuery(cb.id, "Akses ditolak!");
      return;
    }
    
    if (cbData.indexOf("approve:") === 0) {
      // approve:paymentId:userChatId
      var cbParts = cbData.split(":");
      var payId = cbParts[1];
      var userChatId = cbParts[2];
      
      var success = approvePayment(payId, userChatId);
      if (success) {
        answerCallbackQuery(cb.id, "Pembayaran disetujui!");
        editTelegramMessage(cbChatId, msgId, "✅ **📱 Aplikasi: FTTH Basemap**\n**Pembayaran " + payId + " DISETUJUI**");
      } else {
        answerCallbackQuery(cb.id, "Gagal memproses persetujuan!");
      }
    } 
    
    else if (cbData.indexOf("reject:") === 0) {
      var cbParts = cbData.split(":");
      var payId = cbParts[1];
      var userChatId = cbParts[2];
      
      rejectPayment(payId, userChatId);
      answerCallbackQuery(cb.id, "Pembayaran ditolak!");
      editTelegramMessage(cbChatId, msgId, "❌ **📱 Aplikasi: FTTH Basemap**\n**Pembayaran " + payId + " DITOLAK**");
    }
    
    // User detail menu from admin panel list
    else if (cbData.indexOf("user_menu:") === 0) {
      var devId = cbData.split(":")[1];
      sendUserDetailMenu(cbChatId, msgId, devId);
      answerCallbackQuery(cb.id, "Memuat detail...");
    }
    
    // Action revoke/unrevoke
    else if (cbData.indexOf("revoke:") === 0) {
      var devId = cbData.split(":")[1];
      setUserStatus(devId, "Revoked");
      answerCallbackQuery(cb.id, "Pengguna berhasil di-Revoke!");
      sendUserDetailMenu(cbChatId, msgId, devId);
    } 
    
    else if (cbData.indexOf("unrevoke:") === 0) {
      var devId = cbData.split(":")[1];
      setUserStatus(devId, "Active");
      answerCallbackQuery(cb.id, "Pengguna diaktifkan kembali!");
      sendUserDetailMenu(cbChatId, msgId, devId);
    }
    
    else if (cbData === "list_users") {
      var menu = getUsersListInlineMenu();
      editTelegramMessage(cbChatId, msgId, "👥 **Daftar Perangkat Pengguna:**\nPilih untuk Kelola Status:", menu.keyboard);
      answerCallbackQuery(cb.id, "Kembali ke daftar");
    }
  }
}

// ==========================================
// ADMIN LOGIC HELPERS
// ==========================================

function approvePayment(paymentId, userChatId) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var paymentsSheet = ss.getSheetByName("Payments");
  var payData = paymentsSheet.getDataRange().getValues();
  
  var rowIdx = -1;
  var deviceId = "";
  var telegram = "";
  
  for (var i = 1; i < payData.length; i++) {
    if (payData[i][0] === paymentId) {
      rowIdx = i + 1;
      deviceId = payData[i][1];
      telegram = payData[i][2];
      break;
    }
  }
  
  if (rowIdx === -1) return false;
  
  // Extract payment details
  // PaymentID format: FTTH-[suffix]-[txSalt]
  var idParts = paymentId.split("-");
  var txSalt = idParts[2];
  
  // Read durationMonths from column 8 (index 7)
  var durationMonths = 1;
  if (payData[rowIdx - 1].length >= 8 && payData[rowIdx - 1][7] !== undefined && payData[rowIdx - 1][7] !== "") {
    durationMonths = parseInt(payData[rowIdx - 1][7]);
  }
  if (isNaN(durationMonths) || durationMonths <= 0) {
    durationMonths = 1;
  }
  
  // Jika Device ID masih Unknown, batalkan persetujuan karena kunci tidak akan cocok dengan perangkat user asli
  if (deviceId === "Unknown" || !deviceId) {
    sendTelegramMessage(ADMIN_TELEGRAM_ID, "⚠️ **Persetujuan Gagal!**\nDevice ID masih *Unknown*. Pastikan pengguna telah membuka jendela Aktivasi di AutoCAD agar Device ID terkirim ke server sebelum Anda menyetujui pembayaran.");
    return false;
  }
  
  // Save or Update Users Sheet
  var usersSheet = ss.getSheetByName("Users");
  var userData = usersSheet.getDataRange().getValues();
  var userRowIdx = -1;
  
  for (var j = 1; j < userData.length; j++) {
    if (userData[j][0] === deviceId) {
      userRowIdx = j + 1;
      break;
    }
  }
  
  var expiresAt = new Date();
  if (userRowIdx !== -1) {
    // If user is Active and expiry is in the future, extend it
    var currentExpiry = new Date(userData[userRowIdx - 1][3]);
    if (userData[userRowIdx - 1][2] === "Active" && currentExpiry > expiresAt) {
      expiresAt = currentExpiry;
    }
  }
  
  expiresAt.setMonth(expiresAt.getMonth() + durationMonths);
  var expiresStr = expiresAt.toISOString();
  
  // Generate Cryptographic Key
  var activationKey = generateActivationKey(deviceId, txSalt, expiresStr);
  
  // Update Payments Sheet
  paymentsSheet.getRange(rowIdx, 5).setValue("Approved");
  paymentsSheet.getRange(rowIdx, 7).setValue(activationKey);
  
  if (userRowIdx !== -1) {
    usersSheet.getRange(userRowIdx, 2).setValue(telegram);
    usersSheet.getRange(userRowIdx, 3).setValue("Active");
    usersSheet.getRange(userRowIdx, 4).setValue(expiresStr);
    usersSheet.getRange(userRowIdx, 5).setValue(txSalt);
  } else {
    usersSheet.appendRow([deviceId, telegram, "Active", expiresStr, txSalt, new Date()]);
  }
  
  // Notify user via Telegram (safeguarded inside sendTelegramMessage if chatId is "0")
  var userMsg = "✅ **Pembayaran Anda Telah Disetujui!**\n\n" +
                "• Lisensi aktif s/d: **" + expiresAt.toLocaleString("id-ID") + "**\n" +
                "• Perangkat Anda otomatis teraktifasi.\n\n" +
                "🔑 **Serial Key (Jika offline):**\n`" + activationKey + "`";
  sendTelegramMessage(userChatId, userMsg);
  
  return true;
}

function rejectPayment(paymentId, userChatId) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var paymentsSheet = ss.getSheetByName("Payments");
  var payData = paymentsSheet.getDataRange().getValues();
  
  for (var i = 1; i < payData.length; i++) {
    if (payData[i][0] === paymentId) {
      paymentsSheet.getRange(i + 1, 5).setValue("Rejected");
      break;
    }
  }
  
  var userMsg = "❌ **Pembayaran Tagihan " + paymentId + " Ditolak**\n\n" +
                "Admin menolak verifikasi pembayaran ini. Periksa bukti transfer Anda atau hubungi Admin.";
  sendTelegramMessage(userChatId, userMsg);
}

function setUserStatus(deviceId, status) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var usersSheet = ss.getSheetByName("Users");
  var data = usersSheet.getDataRange().getValues();
  for (var i = 1; i < data.length; i++) {
    if (data[i][0] === deviceId) {
      usersSheet.getRange(i + 1, 3).setValue(status);
      break;
    }
  }
}

// ==========================================
// INTERACTIVE KEYBOARD LIST
// ==========================================

function sendUsersListMenu(chatId) {
  var menu = getUsersListInlineMenu();
  sendTelegramMessage(chatId, "👥 **Daftar Perangkat Pengguna:**\nPilih untuk Kelola Status:", menu.keyboard);
}

function getUsersListInlineMenu() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var usersSheet = ss.getSheetByName("Users");
  var data = usersSheet.getDataRange().getValues();
  var buttons = [];
  
  // Generate button for each user
  for (var i = 1; i < data.length; i++) {
    var devId = data[i][0];
    var user = data[i][1];
    var status = data[i][2]; // Active / Revoked
    
    var icon = (status === "Active") ? "🟢" : "🔴";
    var shortDevId = devId.substring(0, 8);
    var label = icon + " " + user + " (" + shortDevId + ")";
    
    buttons.push([{ text: label, callback_data: "user_menu:" + devId }]);
  }
  
  if (buttons.length === 0) {
    buttons.push([{ text: "Tidak ada data pengguna", callback_data: "none" }]);
  }
  
  return { keyboard: { inline_keyboard: buttons } };
}

function sendUserDetailMenu(chatId, messageId, deviceId) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var usersSheet = ss.getSheetByName("Users");
  var telSheet = ss.getSheetByName("Telemetry");
  
  var userData = usersSheet.getDataRange().getValues();
  var userRow = null;
  for (var i = 1; i < userData.length; i++) {
    if (userData[i][0] === deviceId) {
      userRow = userData[i];
      break;
    }
  }
  
  if (!userRow) {
    editTelegramMessage(chatId, messageId, "⚠️ User tidak ditemukan!");
    return;
  }
  
  // Find project count
  var projectsCreated = 0;
  var lastActive = "Never";
  var ver = "N/A";
  var telData = telSheet.getDataRange().getValues();
  for (var j = 1; j < telData.length; j++) {
    if (telData[j][0] === deviceId) {
      projectsCreated = telData[j][1];
      lastActive = new Date(telData[j][2]).toLocaleString("id-ID");
      ver = telData[j][3];
      break;
    }
  }
  
  var expDateStr = new Date(userRow[3]).toLocaleString("id-ID");
  var status = userRow[2];
  
  var text = "👤 **Detail Perangkat Pengguna:**\n\n" +
             "• Device ID: `" + deviceId + "`\n" +
             "• Telegram: " + userRow[1] + "\n" +
             "• Status Lisensi: " + (status === "Active" ? "🟢 **Aktif**" : "🔴 **Revoked (Blokir)**") + "\n" +
             "• Expiration: " + expDateStr + "\n" +
             "• Total Proyek Dibuat: **" + projectsCreated + "**\n" +
             "• Terakhir Aktif: " + lastActive + "\n" +
             "• Versi Plugin: " + ver;
  
  var inlineButtons = [];
  if (status === "Active") {
    inlineButtons.push([{ text: "🛑 Blokir (Revoke)", callback_data: "revoke:" + deviceId }]);
  } else {
    inlineButtons.push([{ text: "✅ Aktifkan Kembali", callback_data: "unrevoke:" + deviceId }]);
  }
  inlineButtons.push([{ text: "⬅ Kembali ke Daftar", callback_data: "list_users" }]);
  
  var keyboard = { inline_keyboard: inlineButtons };
  editTelegramMessage(chatId, messageId, text, keyboard);
}

function getStatsSummary() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var usersSheet = ss.getSheetByName("Users");
  var telSheet = ss.getSheetByName("Telemetry");
  
  var usersData = usersSheet.getDataRange().getValues();
  var telData = telSheet.getDataRange().getValues();
  
  var totalUsers = usersData.length - 1;
  var activeCount = 0;
  var revokedCount = 0;
  
  for (var i = 1; i < usersData.length; i++) {
    if (usersData[i][2] === "Active") {
      activeCount++;
    } else if (usersData[i][2] === "Revoked") {
      revokedCount++;
    }
  }
  
  var totalProjects = 0;
  for (var j = 1; j < telData.length; j++) {
    totalProjects += parseInt(telData[j][1]);
  }
  
  return "📊 **STATISTIK APLIKASI FTTH BASEMAP**\n\n" +
         "• Total Pengguna Terdaftar: **" + totalUsers + "**\n" +
         "• Lisensi Aktif: 🟢 **" + activeCount + "**\n" +
         "• Diblokir (Revoked): 🔴 **" + revokedCount + "**\n" +
         "• Total Proyek Berhasil Dibuat: 🏗 **" + totalProjects + " proyek**\n" +
         "• Diupdate pada: " + new Date().toLocaleString("id-ID");
}

// ==========================================
// CRYPTOGRAPHIC KEY GENERATION
// ==========================================
function generateActivationKey(deviceId, txSalt, expiresAt) {
  var payload = {
    deviceId: deviceId,
    txSalt: txSalt,
    expiresAt: expiresAt
  };
  var payloadJson = JSON.stringify(payload);
  var payloadBase64 = Utilities.base64Encode(payloadJson, Utilities.Charset.UTF_8)
                        .replace(/=/g, ""); // strip padding
  
  // Cryptographic signing with private key
  var signatureBytes = Utilities.computeRsaSha256Signature(payloadJson, PRIVATE_KEY_PEM);
  var signatureBase64 = Utilities.base64Encode(signatureBytes)
                          .replace(/=/g, ""); // strip padding
  
  return payloadBase64 + "." + signatureBase64;
}

// ==========================================
// HTTP TELEGRAM REQUEST HELPERS
// ==========================================
function sendTelegramMessage(chatId, text, replyMarkup) {
  if (!chatId || chatId.toString() === "0" || chatId.toString() === "") {
    return;
  }
  var payload = {
    chat_id: chatId,
    text: text,
    parse_mode: "Markdown"
  };
  if (replyMarkup) {
    payload.reply_markup = JSON.stringify(replyMarkup);
  }
  var options = {
    method: "post",
    contentType: "application/json",
    payload: JSON.stringify(payload)
  };
  UrlFetchApp.fetch("https://api.telegram.org/bot" + BOT_TOKEN + "/sendMessage", options);
}

// Edit Message Text
function editTelegramMessage(chatId, messageId, text, replyMarkup) {
  if (!chatId || chatId.toString() === "0" || chatId.toString() === "") {
    return;
  }
  var payload = {
    chat_id: chatId,
    message_id: messageId,
    text: text,
    parse_mode: "Markdown"
  };
  if (replyMarkup) {
    payload.reply_markup = JSON.stringify(replyMarkup);
  }
  var options = {
    method: "post",
    contentType: "application/json",
    payload: JSON.stringify(payload)
  };
  UrlFetchApp.fetch("https://api.telegram.org/bot" + BOT_TOKEN + "/editMessageText", options);
}

// Answer Callback Query
function answerCallbackQuery(callbackQueryId, text) {
  var payload = {
    callback_query_id: callbackQueryId,
    text: text
  };
  var options = {
    method: "post",
    contentType: "application/json",
    payload: JSON.stringify(payload)
  };
  UrlFetchApp.fetch("https://api.telegram.org/bot" + BOT_TOKEN + "/answerCallbackQuery", options);
}
