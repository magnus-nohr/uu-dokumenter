import { useCallback, useEffect, useRef, useState } from "react";
import { api, ApiError, type Session } from "./api";
import { StartView } from "./views/StartView";
import { JobsView } from "./views/JobsView";
import { ReviewView } from "./views/ReviewView";
import { SettingsView } from "./views/SettingsView";
import { HelpView } from "./views/HelpView";

type Route = { view: "start" } | { view: "jobs"; ids?: string[] } | { view: "review"; id: string } | { view: "settings" } | { view: "help" };

function parse(hash: string): Route {
  const h = hash.replace(/^#\/?/, "");
  const [a, b, c] = h.split("/");
  if (a === "jobber") return { view: "jobs", ids: b ? b.split(",") : undefined };
  if (a === "jobb" && b && c === "gjennomga") return { view: "review", id: b };
  if (a === "innstillinger") return { view: "settings" };
  if (a === "hjelp") return { view: "help" };
  return { view: "start" };
}

export function navigate(path: string) {
  window.location.hash = path;
}

export function App() {
  const [route, setRoute] = useState<Route>(() => parse(window.location.hash));
  const [session, setSession] = useState<Session | null>(null);
  const [error, setError] = useState<string | null>(null);
  const mainRef = useRef<HTMLElement>(null);

  const loadSession = useCallback(async () => {
    try {
      setSession(await api.session());
      setError(null);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Appen kunne ikke starte.");
    }
  }, []);

  useEffect(() => {
    void loadSession();
    const onHash = () => setRoute(parse(window.location.hash));
    window.addEventListener("hashchange", onHash);
    return () => window.removeEventListener("hashchange", onHash);
  }, [loadSession]);

  // Flytt fokus til sidens hovedoverskrift ved navigering, slik at skjermleserbrukere vet hvor de er.
  useEffect(() => {
    const h1 = mainRef.current?.querySelector("h1");
    if (h1) {
      h1.setAttribute("tabindex", "-1");
      h1.focus();
    }
  }, [route]);

  const link = (href: string, label: string, active: boolean) => (
    <li>
      <a href={href} aria-current={active ? "page" : undefined}>
        {label}
      </a>
    </li>
  );

  return (
    <>
      <a className="skip" href="#hovedinnhold" onClick={(e) => { e.preventDefault(); mainRef.current?.focus(); }}>
        Hopp til hovedinnhold
      </a>
      <header className="top">
        <div className="brand">UU-dokumenter</div>
        <nav aria-label="Hovedmeny">
          <ul>
            {link("#/", "Forbedre filer", route.view === "start")}
            {link("#/jobber", "Mine jobber", route.view === "jobs" || route.view === "review")}
            {link("#/innstillinger", "Innstillinger", route.view === "settings")}
            {link("#/hjelp", "Hjelp", route.view === "help")}
          </ul>
        </nav>
      </header>
      <main id="hovedinnhold" ref={mainRef} tabIndex={-1}>
        {error && (
          <div className="alert error" role="alert">
            <p>{error}</p>
            <button type="button" onClick={() => void loadSession()}>Prøv igjen</button>
          </div>
        )}
        {session && route.view === "start" && <StartView session={session} />}
        {session && route.view === "jobs" && <JobsView ids={route.ids} />}
        {session && route.view === "review" && <ReviewView id={route.id} />}
        {session && route.view === "settings" && <SettingsView onSaved={loadSession} />}
        {route.view === "help" && <HelpView />}
        {!session && !error && <p>Laster …</p>}
      </main>
      <footer className="foot">
        <p>
          Alt behandles lokalt på denne maskinen, med mindre du selv velger en skytjeneste. Appen gir forbedringer og en rapport,
          men kan ikke garantere at et dokument er universelt utformet.
        </p>
      </footer>
    </>
  );
}
