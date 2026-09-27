import { Link } from '@tanstack/react-router'
import { Button } from '@/components/ui/button'

export function MaintenanceError() {
  return (
    <div className='h-svh'>
      <div className='m-auto flex h-full w-full flex-col items-center justify-center gap-2'>
        <h1 className='text-[7rem] leading-tight font-bold'>503</h1>
        <span className='font-medium'>采集网关暂时不可用</span>
        <p className='text-center text-muted-foreground'>请稍后重试，或查看 Host 是否仍在运行。</p>
        <div className='mt-6 flex gap-4'>
          <Button asChild variant='outline'>
            <Link to='/'>回到总览</Link>
          </Button>
        </div>
      </div>
    </div>
  )
}
