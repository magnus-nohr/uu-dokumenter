import { describe, expect, it } from "vitest";
import { Job, ReviewItem, Session } from "./api";

describe("API-kontrakter", () => {
  it("godtar gyldig økt", () => {
    const ok = Session.safeParse({
      csrfToken: "abcdefghijklmnop", version: "0.1.0",
      rendering: { available: true, enabled: true, name: "Microsoft Office" }, ocr: { available: true },
      ai: { localConfigured: false, localModel: null, cloudReady: false, cloudModel: "claude-opus-5-5" },
      maxUploadMb: 500, retentionDays: 7,
    });
    expect(ok.success).toBe(true);
  });

  it("avviser ukjent jobbstatus og manglende felt", () => {
    expect(Job.safeParse({ id: "1", name: "a.docx", kind: "docx", status: "hacked", percent: 0, created: "", updated: "" }).success).toBe(false);
    expect(Session.safeParse({ csrfToken: "x" }).success).toBe(false);
  });

  it("krever at forslag har usikkerhet som liste og visningsdata som tekst", () => {
    const base = {
      id: "C0001", kind: "SetAltText", kindText: "Alternativ tekst", group: "Bilder", value: "Planteselle", status: "Venter",
      pending: true, applied: false, discarded: false, editable: true, visibleOnly: false, uncertainty: [], origin: "Ai", display: { location: "Lysbilde 2" },
    };
    expect(ReviewItem.safeParse(base).success).toBe(true);
    expect(ReviewItem.safeParse({ ...base, display: { location: 5 } }).success).toBe(false);
  });
});
