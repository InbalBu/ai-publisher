// Keeps the in-progress article form in IndexedDB so it survives leaving the
// page, switching tabs, or a phone reloading the tab. Cleared only after a
// successful submit or when the browser's site data is wiped. IndexedDB is used
// instead of localStorage because localStorage can't hold image files.

const DB_NAME = 'mekomon-publisher'
const STORE_NAME = 'drafts'
const DRAFT_KEY = 'current'

export interface DraftImage {
  file: File
  /** Photo credit, '' when the operator left it blank. */
  caption: string
}

export interface Draft {
  rawText: string
  useAi: boolean
  title: string
  subtitle: string
  categoryId: number | ''
  images: DraftImage[]
  featuredIndex: number
}

function openDatabase(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, 1)
    request.onupgradeneeded = () => {
      request.result.createObjectStore(STORE_NAME)
    }
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error)
  })
}

async function runTransaction<T>(
  mode: IDBTransactionMode,
  action: (store: IDBObjectStore) => IDBRequest<T>,
): Promise<T> {
  const db = await openDatabase()
  return new Promise<T>((resolve, reject) => {
    const transaction = db.transaction(STORE_NAME, mode)
    const request = action(transaction.objectStore(STORE_NAME))
    transaction.oncomplete = () => {
      db.close()
      resolve(request.result)
    }
    transaction.onerror = () => {
      db.close()
      reject(transaction.error)
    }
    transaction.onabort = () => {
      db.close()
      reject(transaction.error)
    }
  })
}

/** Returns the saved draft, or null when there is none or storage is unavailable. */
export async function loadDraft(): Promise<Draft | null> {
  try {
    const stored = await runTransaction<Draft | undefined>('readonly', (store) => store.get(DRAFT_KEY))
    return stored ?? null
  } catch {
    return null
  }
}

export async function saveDraft(draft: Draft): Promise<void> {
  try {
    await runTransaction('readwrite', (store) => store.put(draft, DRAFT_KEY))
  } catch {
    // Storage full or blocked (private window, disabled site data): the form still works, it just won't persist.
  }
}

export async function clearDraft(): Promise<void> {
  try {
    await runTransaction('readwrite', (store) => store.delete(DRAFT_KEY))
  } catch {
    // Nothing to clean up if storage was never available.
  }
}
