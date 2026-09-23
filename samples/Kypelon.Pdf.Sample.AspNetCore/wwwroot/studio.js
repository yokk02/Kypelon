"use strict";
const form = document.querySelector("#report-form");
const generate = document.querySelector("#generate");
const download = document.querySelector("#download");
const statusText = document.querySelector("#status");
const errorBox = document.querySelector("#error");
const frame = document.querySelector("#pdf-preview");
const openPdf = document.querySelector("#open-pdf");
let current = null;
let active = null;
let serverReady = false;
let revision = 0;

function payload() {
  const values = new FormData(form);
  const template = values.get("template");
  const result = { template, title: values.get("title"), client: values.get("client") || "", notes: values.get("notes"), rows: Number(values.get("rows")), paper: values.get("paper"), orientation: values.get("orientation"), margin: Number(values.get("margin")), compress: values.has("compress"), debugLayout: values.has("debugLayout") };
  if (template === "aefs" || template === "edocket") {
    result.engagementName = values.get("engagementName");
    result.periodEndDate = values.get("periodEndDate");
  }
  if (template === "aefs") Object.assign(result, Object.fromEntries(["recipientName", "requestId", "requestType", "recordUrl"].map(key => [key, values.get(key)])));
  if (template === "edocket") result.docket = {
    id: values.get("docketId"), engagementCode: values.get("engagementCode"), businessUnit: values.get("businessUnit"), country: values.get("country"), partner: values.get("partner"), manager: values.get("manager"), stage: values.get("stage"), archiveDeadline: values.get("archiveDeadline"), repository: values.get("repository"), aatId: values.get("aatId"), generatedAt: values.get("generatedAt"), sourceVersion: Number(values.get("sourceVersion"))
  };
  return result;
}
const defaultNotes = form.elements.notes.value;
function selectTemplate(value) {
  const presets = {
    business: ["FY26 Audit Engagement Report", "50", defaultNotes],
    table: ["Employee Utilisation Report", "500", defaultNotes],
    unicode: ["รายงานทดสอบภาษาไทย / Unicode", "0", defaultNotes],
    aefs: ["Request pending your approval", "0", "A new request has been submitted and is ready for your review. Please review the details below and open the request to continue."]
  };
  presets.edocket = ["Engagement Summary Report", "6", "Reviewer note: The engagement team confirmed that the final archival package will include the approved financial statements, required completion documentation, applicable consultation evidence, and the final eDocket export. This intentionally long paragraph is included to validate line wrapping, page-width calculations, font metrics and paragraph spacing in the PDF export library. The implementation should wrap naturally without clipping, overlap, or horizontal overflow."];
  const preset = presets[value];
  if (!preset) return;
  form.elements.template.value = value;
  form.elements.title.value = preset[0];
  form.elements.rows.value = preset[1];
  form.elements.notes.value = preset[2];
  form.elements.margin.value = value === "aefs" ? "24" : value === "edocket" ? "48" : "36";
  const isAefs = value === "aefs";
  const isDocket = value === "edocket";
  form.elements.client.value = isDocket ? "Aurora Retail Holdings Public Company Limited" : "บริษัท ทดสอบ จำกัด";
  form.elements.engagementName.value = isDocket ? "Aurora Retail Holdings - FY2026 Audit" : "ABC Company Limited – FY26 Audit";
  form.elements.rows.min = isDocket ? "6" : "0";
  document.getElementById("rows-label").textContent = isDocket ? "จำนวนเหตุการณ์ใน Audit Trail" : "จำนวนแถวในตาราง";
  document.getElementById("rows-range").textContent = isDocket ? "6–5,000 รายการ" : "0–5,000 แถว";
  document.getElementById("six-rows").hidden = !isDocket;
  for (const [id, show] of [["aefs-fields", isAefs], ["engagement-fields", isAefs || isDocket], ["docket-fields", isDocket], ["client-field", !isAefs], ["table-fields", !isAefs]]) {
    const fields = document.getElementById(id);
    fields.hidden = !show;
    fields.disabled = !show;
  }
}
function changed() {
  revision++;
  document.querySelector("#stale").hidden = !current;
  document.querySelectorAll("[data-rows]").forEach(button => button.classList.toggle("selected", button.dataset.rows === form.elements.rows.value));
  if (current && !active) statusText.textContent = "ข้อมูลเปลี่ยนแล้ว · กดสร้างตัวอย่างเพื่ออัปเดตไฟล์";
}
function setBusy(busy) {
  document.querySelector("#loading").hidden = !busy;
  document.querySelector("#preview-stage").setAttribute("aria-busy", String(busy));
  generate.disabled = busy || !serverReady;
  download.disabled = busy || !current;
  document.querySelector("#reset").disabled = busy;
}
function formatSize(bytes) { return bytes >= 1048576 ? (bytes / 1048576).toFixed(2) + " MB" : (bytes / 1024).toFixed(1) + " KB"; }
async function createPreview() {
  if (!form.reportValidity() || active || !serverReady) return;
  const request = payload();
  const requestRevision = revision;
  const controller = new AbortController();
  active = controller;
  errorBox.hidden = true;
  setBusy(true);
  statusText.textContent = "กำลังสร้าง PDF…";
  const start = performance.now();
  try {
    const response = await fetch("/api/export", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(request), signal: controller.signal });
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
      const details = problem.errors ? Object.values(problem.errors).flat().join(" · ") : problem.detail || problem.title;
      throw new Error(details || "สร้างเอกสารไม่สำเร็จ กรุณาตรวจสอบข้อมูลแล้วลองอีกครั้ง");
    }
    if (!response.headers.get("content-type")?.includes("application/pdf")) throw new Error("เซิร์ฟเวอร์ไม่ได้ส่งไฟล์ PDF กลับมา");
    const blob = await response.blob();
    if (!blob.size) throw new Error("ได้รับไฟล์ว่าง กรุณาลองใหม่");
    const oldUrl = current?.url;
    current = { url: URL.createObjectURL(blob), filename: "kypelon-" + request.template + ".pdf", bytes: blob.size };
    frame.src = current.url + "#view=FitH";
    frame.hidden = false;
    document.querySelector("#empty-preview").hidden = true;
    openPdf.href = current.url;
    openPdf.hidden = false;
    document.querySelector("#page-count").replaceChildren(document.createTextNode(response.headers.get("X-Kypelon-Pages") || "—"), Object.assign(document.createElement("small"), { textContent: " หน้า" }));
    document.querySelector("#file-size").textContent = formatSize(blob.size);
    document.querySelector("#elapsed").textContent = ((performance.now() - start) / 1000).toFixed(2) + " s";
    document.querySelector("#file-state").textContent = request.paper.toUpperCase() + " · " + (request.orientation === "portrait" ? "แนวตั้ง" : "แนวนอน");
    document.querySelector("#stale").hidden = revision === requestRevision;
    statusText.textContent = revision === requestRevision ? "สร้างสำเร็จ · ตัวอย่างและไฟล์ดาวน์โหลดเป็น PDF เดียวกัน" : "สร้างไฟล์สำเร็จ · ข้อมูลมีการแก้ไข กดสร้างอีกครั้งเพื่ออัปเดต";
    if (oldUrl) setTimeout(() => URL.revokeObjectURL(oldUrl), 1000);
  } catch (error) {
    if (error.name === "AbortError") statusText.textContent = "ยกเลิกการสร้างแล้ว";
    else { errorBox.textContent = error.message; errorBox.hidden = false; statusText.textContent = current ? "สร้างไฟล์ใหม่ไม่สำเร็จ · ยังแสดงไฟล์ก่อนหน้า" : "ยังสร้างเอกสารไม่สำเร็จ"; }
  } finally {
    active = null;
    setBusy(false);
  }
}
form.addEventListener("submit", event => { event.preventDefault(); createPreview(); });
form.addEventListener("input", changed);
form.addEventListener("change", event => {
  if (event.target.name === "template") {
    selectTemplate(event.target.value);
    changed();
  }
});
document.querySelectorAll("[data-rows]").forEach(button => button.addEventListener("click", () => { form.elements.rows.value = button.dataset.rows; changed(); }));
document.querySelector("#reset").addEventListener("click", () => { const template = form.elements.template.value; form.reset(); selectTemplate(template); changed(); errorBox.hidden = true; });
document.querySelector("#cancel").addEventListener("click", () => active?.abort());
download.addEventListener("click", () => {
  if (!current || active) return;
  const link = document.createElement("a");
  link.href = current.url; link.download = current.filename;
  document.body.append(link); link.click(); link.remove();
  statusText.textContent = "ส่งไฟล์ " + current.filename + " ไปยังรายการดาวน์โหลดแล้ว";
});
window.addEventListener("pagehide", () => { active?.abort(); if (current) URL.revokeObjectURL(current.url); });
async function initialize() {
  try {
    const response = await fetch("/api/status");
    if (!response.ok) throw new Error("เชื่อมต่อ Kypelon ไม่สำเร็จ");
    const info = await response.json();
    if (!info.ready) throw new Error("ยังไม่มีฟอนต์ภาษาไทย: ตั้งค่า KYPELON_FONT เป็นไฟล์ TrueType แล้วเริ่มเว็บใหม่");
    document.querySelector("#font-name").textContent = info.font;
    const connection = document.querySelector("#connection");
    connection.classList.add("ready");
    connection.replaceChildren(document.createElement("i"), document.createTextNode(" Engine พร้อมใช้งาน"));
    serverReady = true;
    setBusy(false);
    await createPreview();
  } catch (error) {
    document.querySelector("#connection").classList.add("offline");
    document.querySelector("#connection").replaceChildren(document.createElement("i"), document.createTextNode(" ยังไม่พร้อมใช้งาน"));
    errorBox.textContent = error.message; errorBox.hidden = false;
    statusText.textContent = "ไม่สามารถเตรียมตัวอย่างได้ กรุณาตรวจสอบเซิร์ฟเวอร์แล้วรีเฟรชหน้า";
  }
}
selectTemplate(new URLSearchParams(window.location.search).get("template") || "business");
initialize();
