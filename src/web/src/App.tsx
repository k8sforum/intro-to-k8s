import { Link, Route, Routes, useLocation } from 'react-router-dom';
import { useBooleanFlagValue } from '@openfeature/react-sdk';
import { MapPage } from './components/MapPage';
import { TraceabilityPage } from './components/TraceabilityPage';
import { FailedMessagesPage } from './components/FailedMessagesPage';
import { PostmarkGlyph } from './components/icons';

function NavLink({ to, label }: { to: string; label: string }) {
  return (
    <Link
      to={to}
      className="ml-1 rounded border border-brass/40 px-2 py-1 font-mono text-[10px] tracking-wide text-brass uppercase transition hover:border-postmark hover:text-postmark dark:hover:text-postmark-light"
    >
      {label}
    </Link>
  );
}

function Header() {
  const location = useLocation();
  const onMap = location.pathname === '/';
  const messageTracingEnabled = useBooleanFlagValue('enable-message-tracing', true);
  const failedMessageAdminEnabled = useBooleanFlagValue('enable-failed-message-admin', true);

  return (
    <header className="absolute top-4 right-4 z-[500] sm:top-5 sm:right-5">
      <div className="flex items-center gap-2.5 rounded-md border border-brass/40 bg-paper/95 px-3.5 py-2 shadow-md backdrop-blur-sm dark:border-brass/30 dark:bg-harbor-2/95">
        <PostmarkGlyph className="h-4.5 w-4.5 shrink-0 text-postmark dark:text-postmark-light" />
        <h1 className="leading-tight">
          <span className="block font-display text-[15px] font-medium tracking-tight text-ink italic dark:text-bone">
            MyTravels
          </span>
          <span className="block font-mono text-[9px] tracking-[0.18em] text-brass uppercase">
            Field Log
          </span>
        </h1>
        {onMap ? (
          <>
            {messageTracingEnabled && <NavLink to="/traceability" label="Traceability" />}
            {failedMessageAdminEnabled && (
              <NavLink to="/admin/failed-messages" label="Failed Messages" />
            )}
          </>
        ) : (
          <NavLink to="/" label="Map" />
        )}
      </div>
    </header>
  );
}

function App() {
  return (
    <div className="relative h-svh w-full">
      <Header />
      <Routes>
        <Route path="/" element={<MapPage />} />
        <Route path="/traceability" element={<TraceabilityPage />} />
        <Route path="/admin/failed-messages" element={<FailedMessagesPage />} />
      </Routes>
    </div>
  );
}

export default App;
