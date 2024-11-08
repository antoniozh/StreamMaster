import SMTasksDataSelector from '@components/smtasks/SMTasksDataSelector';
import './ball-beat.css';

const SMLoader = () => {
  return (
    <div className="sm-loader sm-modal-20 z-20">
      <div className=" flex flex-column justify-content-center align-items-center align-content-center">
        <SMTasksDataSelector />
        <div className="la-ball-beat la-2x w-full flex justify-content-center align-items-center z-20 surface-ground">
          <div></div>
          <div></div>
          <div></div>
          <div></div>
          <div></div>
          <div></div>
          <div></div>
        </div>
      </div>
    </div>
  );

  // return (
  //   <div className="sm-loader">
  //     <div className="flex flex-column justify-content-center align-items-center align-content-center">
  //       <div className="la-ball-spin-clockwise-fade la-3x">
  //         <div></div>
  //         <div></div>
  //         <div></div>
  //         <div></div>
  //         <div></div>
  //         <div></div>
  //         <div></div>
  //         <div></div>
  //       </div>
  //     </div>
  //   </div>
  // );
};

export default SMLoader;
