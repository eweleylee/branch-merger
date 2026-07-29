import { ref } from 'vue'

// Global "a merge is in progress" flag. While true, the UI disables actions that
// could collide with the running merge (a second Merge now, refresh/fetch, schedule
// changes, settings, etc.), so nothing can be triggered twice mid-process.
export const busy = ref(false)
