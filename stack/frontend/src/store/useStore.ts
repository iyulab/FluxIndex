import { create } from 'zustand'
import { persist } from 'zustand/middleware'

interface AppState {
  apiKey: string | null
  setApiKey: (key: string | null) => void
  selectedCollectionId: string | null
  setSelectedCollectionId: (id: string | null) => void
  theme: 'light' | 'dark'
  setTheme: (theme: 'light' | 'dark') => void
}

export const useStore = create<AppState>()(
  persist(
    (set) => ({
      apiKey: null,
      setApiKey: (key) => {
        set({ apiKey: key })
        if (key) {
          localStorage.setItem('fluxindex-api-key', key)
        } else {
          localStorage.removeItem('fluxindex-api-key')
        }
      },
      selectedCollectionId: null,
      setSelectedCollectionId: (id) => set({ selectedCollectionId: id }),
      theme: 'light',
      setTheme: (theme) => {
        set({ theme })
        document.documentElement.classList.toggle('dark', theme === 'dark')
      },
    }),
    {
      name: 'fluxindex-storage',
      partialize: (state) => ({
        apiKey: state.apiKey,
        theme: state.theme,
        // selectedCollectionId is session-only (search scope filter)
      }),
    }
  )
)
