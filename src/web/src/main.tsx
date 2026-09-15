import './telemetry'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { OpenFeature } from '@openfeature/web-sdk'
import { OpenFeatureProvider } from '@openfeature/react-sdk'
import { FlagsmithClientProvider } from '@openfeature/flagsmith-client-provider'
import './index.css'
import App from './App.tsx'

await OpenFeature.setProviderAndWait(
  new FlagsmithClientProvider({
    environmentID: import.meta.env.VITE_FLAGSMITH_ENVIRONMENT_ID,
    api: import.meta.env.VITE_FLAGSMITH_API_URL,
  }),
)

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <OpenFeatureProvider>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </OpenFeatureProvider>
  </StrictMode>,
)
