import { z } from "zod";

// Validerte kontrakter mot backend. Uventede svar gir en forståelig feil i stedet for udefinert oppførsel.

export const Session = z.object({
  csrfToken: z.string().min(10),
  version: z.string().nullable().optional(),
  rendering: z.object({ available: z.boolean(), enabled: z.boolean(), name: z.string() }),
  ocr: z.object({ available: z.boolean() }),
  ai: z.object({
    localConfigured: z.boolean(),
    localModel: z.string().nullable().optional(),
    cloudReady: z.boolean(),
    cloudModel: z.string(),
  }),
  maxUploadMb: z.number(),
  retentionDays: z.number(),
});
export type Session = z.infer<typeof Session>;

export const Job = z.object({
  id: z.string(),
  name: z.string(),
  kind: z.string(),
  status: z.enum(["queued", "running", "done", "failed", "rejected", "cancelled", "reviewing"]),
  stage: z.string().nullable().optional(),
  percent: z.number(),
  detail: z.string().nullable().optional(),
  error: z.string().nullable().optional(),
  errorCode: z.string().nullable().optional(),
  created: z.string(),
  updated: z.string(),
});
export type Job = z.infer<typeof Job>;

export const JobDetails = z.object({
  job: Job,
  summary: z
    .object({
      sentences: z.array(z.string()),
      pendingReview: z.number(),
      remaining: z.number(),
      visual: z.string(),
      hasVisibleVariant: z.boolean(),
      changed: z.number(),
      kind: z.string(),
    })
    .nullable(),
});
export type JobDetails = z.infer<typeof JobDetails>;

export const UploadResult = z.object({
  jobs: z.array(
    z.object({
      name: z.string(),
      id: z.string().optional(),
      error: z.string().nullable().optional(),
      code: z.string().nullable().optional(),
      signed: z.boolean().optional(),
      isProtected: z.boolean().optional(),
    }),
  ),
});

export const ReviewItem = z.object({
  id: z.string(),
  kind: z.string(),
  kindText: z.string(),
  group: z.string(),
  value: z.string(),
  oldValue: z.string().nullable().optional(),
  status: z.string(),
  pending: z.boolean(),
  applied: z.boolean(),
  discarded: z.boolean(),
  editable: z.boolean(),
  visibleOnly: z.boolean(),
  reason: z.string().nullable().optional(),
  statusNote: z.string().nullable().optional(),
  uncertainty: z.array(z.string()),
  origin: z.string(),
  image: z.string().nullable().optional(),
  display: z.record(z.string(), z.string()),
});
export type ReviewItem = z.infer<typeof ReviewItem>;
export const ReviewList = z.object({ items: z.array(ReviewItem) });

export const Settings = z.object({
  localAiUrl: z.string(),
  localAiModel: z.string().nullable().optional(),
  cloudProvider: z.string(),
  cloudModel: z.string(),
  cloudConsent: z.boolean(),
  cloudConsentAt: z.string().nullable().optional(),
  cloudKeyStored: z.boolean(),
  secureStorage: z.boolean(),
  retentionDays: z.number(),
  rendering: z.boolean(),
  defaultProfile: z.string(),
  consentText: z.string(),
});
export type Settings = z.infer<typeof Settings>;

export type Decision = { changeId: string; action: "accept" | "edit" | "keep" | "skip" | "undo"; value?: string };

let csrf = "";

export class ApiError extends Error {}

async function call<T>(schema: z.ZodType<T>, url: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (init.method && init.method !== "GET") headers.set("X-UuDok-Csrf", csrf);
  let res: Response;
  try {
    res = await fetch(url, { ...init, headers, credentials: "same-origin" });
  } catch {
    throw new ApiError("Får ikke kontakt med appen. Kjører den fortsatt? Start den på nytt med start.bat.");
  }
  const body: unknown = await res.json().catch(() => null);
  if (!res.ok) {
    const msg = body && typeof body === "object" && "error" in body ? String((body as { error: unknown }).error) : `Feil fra appen (${res.status}).`;
    throw new ApiError(msg);
  }
  const parsed = schema.safeParse(body);
  if (!parsed.success) throw new ApiError("Appen svarte i et uventet format. Last inn siden på nytt.");
  return parsed.data;
}

const Ok = z.object({ ok: z.boolean() }).passthrough();

export const api = {
  async session() {
    const s = await call(Session, "/api/session");
    csrf = s.csrfToken;
    return s;
  },
  upload(files: File[], mode: string, aiMode: string, ai: string, allowSig: boolean) {
    const fd = new FormData();
    files.forEach((f) => fd.append("files", f, f.name));
    fd.append("mode", mode);
    fd.append("aiMode", aiMode);
    fd.append("ai", ai);
    fd.append("allowSignatureInvalidation", String(allowSig));
    return call(UploadResult, "/api/jobs", { method: "POST", body: fd });
  },
  jobs: () => call(z.array(Job), "/api/jobs"),
  job: (id: string) => call(JobDetails, `/api/jobs/${encodeURIComponent(id)}`),
  cancel: (id: string) => call(Ok, `/api/jobs/${encodeURIComponent(id)}/cancel`, { method: "POST" }),
  remove: (id: string) => call(Ok, `/api/jobs/${encodeURIComponent(id)}`, { method: "DELETE" }),
  review: (id: string) => call(ReviewList, `/api/jobs/${encodeURIComponent(id)}/review`),
  submitReview: (id: string, decisions: Decision[]) =>
    call(Ok, `/api/jobs/${encodeURIComponent(id)}/review`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ decisions }),
    }),
  settings: () => call(Settings, "/api/settings"),
  saveSettings: (s: Partial<Settings> & { cloudConsent?: boolean }) =>
    call(Settings, "/api/settings", { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify(s) }),
  saveKey: (key: string) =>
    call(Ok, "/api/settings/cloud-key", { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ key }) }),
  deleteKey: () => call(Ok, "/api/settings/cloud-key", { method: "DELETE" }),
  testLocal: () => call(z.object({ ok: z.boolean(), models: z.array(z.string()).optional(), error: z.string().optional() }), "/api/settings/test-local", { method: "POST" }),
};

export const isFinished = (s: Job["status"]) => s === "done" || s === "failed" || s === "rejected" || s === "cancelled";
