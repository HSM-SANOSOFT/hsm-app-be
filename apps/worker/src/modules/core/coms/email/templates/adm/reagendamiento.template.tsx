import { ReagendamientoTemplateDto } from '@hsm-lib/definitions/dtos';
export function ReagendamientoTemplate(
  data: ReagendamientoTemplateDto,
): React.ReactNode {
  return (
    <>
      <p>
        {' '}
        Estimad@ paciente: <b>{data.patientName}</b>{' '}
      </p>
      <p>
        {' '}
        Le saludamos del Hospital Santamaría, para notificarle que, debido a un
        impedimento médico, su cita medica del día <b>{data.scheduledDate}</b>{' '}
        fue reagendada para:{' '}
      </p>
      <p>
        {' '}
        El dia: <b>{data.rescheduledDate}</b>{' '}
      </p>
      <p>
        {' '}
        Hora: <b>{data.appointmentTime}</b>{' '}
      </p>
      <p>
        {' '}
        Turno: <b>{data.appointmentDetails}</b>{' '}
      </p>
      <p>
        {' '}
        Especialidad: <b>{data.medicalSpecialty}</b>{' '}
      </p>
      <p>{data.appointmentType}.</p>
      <p>
        {' '}
        Agradecemos su comprensión y quedamos a su disposición para cualquier
        consulta adicional, escriba al numero 0980892296 de nuestro asistente
        virtual SANTI
      </p>
      <p>Atentamente,</p>
      <p>Servicio al Paciente</p>
      <p>Celular 0981764904</p>
    </>
  );
}
